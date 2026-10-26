using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Axis.Administration;

/// <summary>The outcome of <see cref="PlatformRekey.RunAsync"/>. <see cref="Problem"/> is <see langword="null"/> on success.</summary>
public sealed record PlatformRekeyResult(int Exit, string? Problem, int TenantSecrets, int DataProtectionKeys);

/// <summary>
/// Re-encrypts every secret of the platform database with <c>Platform:SecretKey</c>. It decrypts each tenant secret and each
/// data protection key with the current key or else the previous key, and encrypts it again with the current key, in one
/// transaction. The stored format and the associated data do not change. The command line and a later web console both call it.
/// </summary>
public static class PlatformRekey
{
    /// <summary>
    /// Runs the rekey. Exit 0 means every value was re-encrypted. Exit 2 means a value decrypts with neither key and nothing
    /// changed, not even the audit table. Exit 1 means the audit record or the update failed and no value changed.
    /// A <see cref="PlatformRekeyResult.Problem"/> never holds a value.
    /// </summary>
    public static async Task<PlatformRekeyResult> RunAsync(
        string platformConnectionString,
        TenantSecretCipher cipher,
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        await using var platform = NewContext(platformConnectionString);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);

        // A concurrent admin command or a server key insert waits for the commit, instead of writing a row that this run overwrites.
        // Readers are not blocked.
        await platform.Database.ExecuteSqlRawAsync(
            "LOCK TABLE platform.tenants, platform.tenant_bindings, platform.data_protection_keys IN EXCLUSIVE MODE",
            cancellationToken);

        var tenants = await platform.Tenants.ToListAsync(cancellationToken);
        var bindings = await platform.TenantBindings.Where(binding => binding.Kind == "secret").ToListAsync(cancellationToken);
        var keys = await platform.DataProtectionKeys.ToListAsync(cancellationToken);

        // Every value decrypts before anything is written, so a value that neither key opens stops the run with no change.
        var steps = new List<Action>();
        var versions = new HashSet<string>(StringComparer.Ordinal);
        int tenantSecrets;
        try
        {
            foreach (var tenant in tenants)
            {
                var row = tenant;
                Plan(steps, versions, row.Id, row.RuntimePassword, TenantSecretFields.RuntimePassword, value => row.RuntimePassword = value);
                Plan(steps, versions, row.Id, row.SchemaPassword, TenantSecretFields.SchemaPassword, value => row.SchemaPassword = value);
                Plan(steps, versions, row.Id, row.OidcClientSecret, TenantSecretFields.OidcClientSecret, value => row.OidcClientSecret = value);
                Plan(steps, versions, row.Id, row.OidcClientKey, TenantSecretFields.OidcClientKey, value => row.OidcClientKey = value);
                Plan(steps, versions, row.Id, row.OidcPreviousClientKey, TenantSecretFields.OidcClientKey, value => row.OidcPreviousClientKey = value);
            }

            foreach (var binding in bindings)
            {
                var row = binding;
                Plan(steps, versions, row.TenantId, row.SecretValue, TenantSecretFields.Binding(row.Name), value => row.SecretValue = value);
            }

            tenantSecrets = steps.Count;
            foreach (var key in keys)
            {
                var row = key;
                if (row.Xml.Length == 0)
                {
                    continue;
                }

                var xml = cipher.DecryptDataProtectionKey(row.Id, row.Xml).Reveal();
                steps.Add(() => row.Xml = cipher.EncryptDataProtectionKey(row.Id, xml));
            }

        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new PlatformRekeyResult(2, exception.Message, 0, 0);
        }

        return await WriteAsync(platform, transaction, platformConnectionString, command, tenants, steps, versions, tenantSecrets, steps.Count - tenantSecrets, cancellationToken);

        // Decrypts at once, and queues the re-encryption for the write phase.
        void Plan(List<Action> queue, HashSet<string> changedTenants, string tenantId, byte[]? stored, string field, Action<byte[]> assign)
        {
            if (stored is null || stored.Length == 0)
            {
                return;
            }

            var plain = cipher.Decrypt(tenantId, field, stored).Reveal();
            changedTenants.Add(tenantId);
            queue.Add(() => assign(cipher.Encrypt(tenantId, field, plain)));
        }
    }

    private static async Task<PlatformRekeyResult> WriteAsync(
        PlatformDbContext platform,
        IDbContextTransaction transaction,
        string platformConnectionString,
        string command,
        List<TenantRow> tenants,
        List<Action> steps,
        HashSet<string> versions,
        int tenantSecrets,
        int keyCount,
        CancellationToken cancellationToken)
    {
        // The record is written on its own connection, before the first update, and stays even if the update later fails.
        try
        {
            await using var audit = NewContext(platformConnectionString);
            await PlatformAuditRecords.WriteAsync(
                audit,
                command,
                tenantId: null,
                [$"tenant secrets: {tenantSecrets}", $"data protection keys: {keyCount}"],
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new PlatformRekeyResult(1, $"audit failed: {Safe(exception)}", 0, 0);
        }

        try
        {
            foreach (var step in steps)
            {
                step();
            }

            // Every directory change raises the version, so a host loads the new ciphertext at its next refresh.
            foreach (var tenant in tenants.Where(tenant => versions.Contains(tenant.Id)))
            {
                tenant.Version++;
            }

            await platform.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new PlatformRekeyResult(1, $"The rekey failed and changed nothing: {Safe(exception)}", 0, 0);
        }

        return new PlatformRekeyResult(0, null, tenantSecrets, keyCount);
    }

    private static PlatformDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);

    // A save failure wraps the database error, whose message is the one that helps the operator. A message never holds a value.
    private static string Safe(Exception exception) =>
        (exception is DbUpdateException { InnerException: { } inner } ? inner : exception) switch
        {
            PostgresException postgres => postgres.MessageText,
            InvalidOperationException invalid => invalid.Message,
            var other => other.GetType().Name,
        };
}
