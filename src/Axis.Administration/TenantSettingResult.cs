using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Administration;

/// <summary>How a tenant setting command ended.</summary>
public enum TenantSettingOutcome
{
    /// <summary>The change and its platform audit record were written in one transaction.</summary>
    Changed,

    /// <summary>A rule refused the command. Nothing changed and no record was written.</summary>
    Refused,

    /// <summary>The platform audit record could not be written. Nothing changed.</summary>
    AuditFailed,

    /// <summary>The change failed. Nothing changed and no record was written.</summary>
    Failed,
}

/// <summary>
/// The result of a tenant setting command. <paramref name="Message"/> names tenant ids, binding names and hosts, and never a
/// value. It is set for every outcome but <see cref="TenantSettingOutcome.Changed"/>.
/// </summary>
public sealed record TenantSettingResult(TenantSettingOutcome Outcome, string? Message = null)
{
    public static TenantSettingResult Changed { get; } = new(TenantSettingOutcome.Changed);

    public static TenantSettingResult Refused(string message) => new(TenantSettingOutcome.Refused, message);
}

/// <summary>What the tenant setting commands share: the checks of a tenant and the transaction of a change.</summary>
internal static class TenantSettingWrites
{
    public const string Provisioning = "provisioning";

    public const string UnknownTenant = "The tenant does not exist.";

    /// <summary>
    /// Returns the reason the tenant cannot be changed, or <see langword="null"/> when it can. A provisioning tenant is
    /// refused: a rerun of <c>tenant create</c> or <c>tenant restore</c> replaces its bindings and hosts, so a change made
    /// before then would be lost without a word.
    /// </summary>
    public static async Task<string?> ChangeProblemAsync(PlatformDbContext platform, string tenantId, CancellationToken cancellationToken)
    {
        var status = await platform.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return status switch
        {
            null => UnknownTenant,
            Provisioning => $"The tenant '{tenantId}' is still provisioning. Finish tenant create first.",
            _ => null,
        };
    }

    /// <summary>
    /// Writes the audit record, runs <paramref name="change"/>, raises the version of <paramref name="tenantIds"/> and commits, in
    /// one transaction. The record goes first, so that a record that cannot be written leaves nothing changed, and a record
    /// always means the change was made. <paramref name="change"/> only tracks its changes, and this method saves them.
    /// </summary>
    public static async Task<TenantSettingResult> WriteAsync(
        PlatformDbContext platform,
        string command,
        string auditTenantId,
        IReadOnlyList<string> changed,
        IReadOnlyList<string> tenantIds,
        Func<CancellationToken, Task<TenantSettingResult?>> change,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await PlatformAuditRecords.WriteAsync(platform, command, auditTenantId, changed, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A save failure wraps the database error, whose message is the one that helps the operator.
                var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
                return new TenantSettingResult(TenantSettingOutcome.AuditFailed, Safe(cause));
            }

            var refusal = await change(cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            await platform.SaveChangesAsync(cancellationToken);
            var ids = tenantIds.ToArray();
            await platform.Tenants
                .Where(tenant => ids.Contains(tenant.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(tenant => tenant.Version, tenant => tenant.Version + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TenantSettingResult.Changed;
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            return TenantSettingResult.Refused(conflictMessage);
        }
        catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or InvalidOperationException or OperationCanceledException)
        {
            var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
            return new TenantSettingResult(TenantSettingOutcome.Failed, Safe(cause));
        }
    }

    private static bool IsUniqueViolation(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }
        or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Never ToString(): an Npgsql message can name the host.
    private static string Safe(Exception exception) => exception switch
    {
        OperationCanceledException => "canceled",
        InvalidOperationException invalid => invalid.Message,
        PostgresException postgres => postgres.MessageText,
        _ => exception.GetType().Name,
    };
}
