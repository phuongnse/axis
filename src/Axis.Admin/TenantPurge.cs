using System.Text.Json;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The purge of a deleted tenant, step 3 of deleting a tenant, as eight named <see cref="JobStep"/>s in this order: <c>hosts</c>, <c>database</c>,
/// <c>realm</c>, <c>files</c>, <c>studio</c>, <c>keys</c>, <c>rows</c> and <c>directory</c>. <c>hosts</c> first writes the tombstone, one row of
/// <c>platform.deleted_tenants</c> with the id, the time, the actor from the job input <c>{ "actor" }</c> and the hosts, and refuses the job when the
/// input names no actor. It then deletes the tenant's hosts and its realm's redirect addresses. <c>database</c> drops the database and the schema and
/// runtime roles, <c>realm</c> deletes the realm, <c>files</c> does nothing until files outside the database exist, <c>studio</c> deletes the Studio
/// folder, <c>keys</c> destroys the data, lookup and backup keys, which is the crypto-shredding, <c>rows</c> deletes the quota, lease, usage and counter
/// rows, and <c>directory</c> deletes the tenant row, which cascades to the rest. When the tenant has a preview tenant, each step runs for the preview
/// first, and the preview gets a tombstone of its own. Every part is found from the id alone, so a part already gone counts as done and a purge that
/// stopped finishes when it runs again. Platform audit records and job rows that name the tenant stay. It checks no id prefix and touches only the tenant
/// and its preview. An error never carries a host or a connection string.
/// </summary>
internal sealed class TenantPurge(
    NpgsqlConnectionStringBuilder admin,
    string platformConnectionString,
    TenantSecretCipher cipher,
    HttpClient http,
    IdentityProviderAdmin identityProvider,
    string? studioApplicationsPath)
{
    private const string NoActor = "The job input names no actor.";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private const string TombstoneSql =
        """
        INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts)
        VALUES (@id, clock_timestamp(), @actor, coalesce((SELECT array_agg(host ORDER BY host) FROM platform.tenant_hosts WHERE tenant_id = @id), '{}'))
        ON CONFLICT (id) DO NOTHING
        """;

    public IReadOnlyList<JobStep> Steps() =>
    [
        new JobStep("hosts", (_, id, input, ct) => HostsAsync(id, input, ct)),
        new JobStep("database", (_, id, _, ct) => EachPartAsync(id, part => TenantPartRemoval.RemoveDatabaseAndRolesAsync(admin, part, ct), ct)),
        new JobStep("realm", (_, id, _, ct) => EachPartAsync(id, part => new TenantRealm(http, identityProvider).DeleteAsync(part, ct), ct)),
        new JobStep("files", (_, _, _, _) => Task.FromResult<string?>(null)),
        new JobStep("studio", (_, id, _, ct) => EachPartAsync(id, part => { TenantPartRemoval.RemoveStudioFolder(studioApplicationsPath, part); return Task.CompletedTask; }, ct)),
        new JobStep("keys", (_, id, _, ct) => EachPartAsync(id, part => KeysAsync(part, ct), ct)),
        new JobStep("rows", (_, id, _, ct) => EachPartAsync(id, part => RowsAsync(part, ct), ct)),
        new JobStep("directory", (_, id, _, ct) => EachPartAsync(id, part => DirectoryAsync(part, ct), ct)),
    ];

    private PlatformDbContext NewPlatform() => new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);

    // The preview tenant goes first: its row points at the main tenant, so the main tenant's row cannot go before it.
    private async Task<IReadOnlyList<string>> PartsAsync(string id, CancellationToken cancellationToken)
    {
        await using var platform = NewPlatform();
        var previews = await platform.Tenants.AsNoTracking().Where(tenant => tenant.PreviewOf == id).Select(tenant => tenant.Id).ToListAsync(cancellationToken);
        return [.. previews, id];
    }

    private async Task<string?> EachPartAsync(string id, Func<string, Task> step, CancellationToken cancellationToken)
    {
        foreach (var part in await PartsAsync(id, cancellationToken))
        {
            await step(part);
        }

        return null;
    }

    private async Task<string?> HostsAsync(string id, string? input, CancellationToken cancellationToken)
    {
        var actor = ReadActor(input);
        if (actor is null)
        {
            return NoActor;
        }

        foreach (var part in await PartsAsync(id, cancellationToken))
        {
            await RemoveHostsAsync(part, actor, cancellationToken);
            await ClearRedirectAddressesAsync(part, cancellationToken);
        }

        return null;
    }

    private static string? ReadActor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PurgeInput>(input, _json)?.Actor is { } actor && !string.IsNullOrWhiteSpace(actor) ? actor : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The tombstone and the delete are one transaction, so the hosts are never gone without a tombstone that holds them.
    private async Task RemoveHostsAsync(string id, string actor, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var tombstone = new NpgsqlCommand(TombstoneSql, connection, transaction))
        {
            tombstone.Parameters.AddWithValue("id", id);
            tombstone.Parameters.AddWithValue("actor", actor);
            await tombstone.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = new NpgsqlCommand("DELETE FROM platform.tenant_hosts WHERE tenant_id = @id", connection, transaction))
        {
            delete.Parameters.AddWithValue("id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Writes no redirect address to the tenant's realm, as the host commands do with the hosts. A tenant without a realm of its own, such as a preview
    // tenant, a row that is already gone or a realm that is already gone, has nothing to clear.
    private async Task ClearRedirectAddressesAsync(string id, CancellationToken cancellationToken)
    {
        TenantRow? row;
        await using (var platform = NewPlatform())
        {
            row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
        }

        if (row is null || DirectoryTenantCatalog.Realm(row, cipher) is not { } realm || !await new TenantRealm(http, identityProvider).ExistsAsync(id, cancellationToken))
        {
            return;
        }

        await new RealmSync(http, realm.BaseAddress).SyncPlatformAddressesAsync(realm.Name, realm.ActivationClientSecret.Reveal(), [], cancellationToken);
    }

    private async Task KeysAsync(string id, CancellationToken cancellationToken)
    {
        await using var platform = NewPlatform();
        await platform.TenantDataKeys.Where(key => key.TenantId == id).ExecuteDeleteAsync(cancellationToken);
        await platform.TenantBackupKeys.Where(key => key.TenantId == id).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task RowsAsync(string id, CancellationToken cancellationToken)
    {
        await using var platform = NewPlatform();
        await platform.TenantQuotas.Where(row => row.TenantId == id).ExecuteDeleteAsync(cancellationToken);
        await platform.TenantLeases.Where(row => row.TenantId == id).ExecuteDeleteAsync(cancellationToken);
        await platform.TenantUsage.Where(row => row.TenantId == id).ExecuteDeleteAsync(cancellationToken);
        await platform.TenantUsageLatest.Where(row => row.TenantId == id).ExecuteDeleteAsync(cancellationToken);
        await platform.TenantDailyCounters.Where(row => row.TenantId == id).ExecuteDeleteAsync(cancellationToken);
    }

    // The row cascades to the host requests, bindings, platform providers, legal links and backup time.
    private async Task DirectoryAsync(string id, CancellationToken cancellationToken)
    {
        await using var platform = NewPlatform();
        await platform.Tenants.Where(tenant => tenant.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    private sealed record PurgeInput(string? Actor);
}
