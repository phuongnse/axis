using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Admin;

/// <summary>
/// <c>migrate</c>: brings each tenant database to the schema of this build, on its own. For each tenant it grants, migrates,
/// grants again, checks the runtime role and records the schema version in the directory, in the same save that makes the tenant's
/// data key 1 or lookup key 1 when it has none. A tenant stops at its first failed
/// step and keeps its old schema version, and the other tenants go on. Each step writes one line that names the tenant and
/// never a password, a host or a connection string. A tenant is migrated as its own schema role, over the host and port that the
/// directory records for it.
/// </summary>
internal sealed class TenantMigrate(
    PlatformSettings settings,
    IReadOnlyList<TenantRow> tenants,
    int parallel,
    TenantAudit audit,
    TextWriter output,
    CancellationToken cancellationToken)
{
    private readonly TextWriter _output = TextWriter.Synchronized(output);

    public async Task<int> RunAsync()
    {
        var failed = 0;
        try
        {
            await Parallel.ForEachAsync(
                tenants,
                new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken },
                async (row, _) =>
                {
                    if (!await MigrateTenantAsync(row))
                    {
                        Interlocked.Increment(ref failed);
                    }
                });
        }
        catch (OperationCanceledException)
        {
            return 1;
        }

        return failed == 0 ? 0 : 1;
    }

    private async Task<bool> MigrateTenantAsync(TenantRow row)
    {
        if (!await audit.WriteAsync(row.Id, _output, cancellationToken))
        {
            return false;
        }

        await using var steps = new TenantDatabaseSteps(settings.Cipher, row, row.DatabaseHost, row.DatabasePort, TenantDatabaseTls.From(settings.PlatformConnectionString), cancellationToken);
        return await Step(row.Id, "grants", steps.GrantAsync)
            && await Step(row.Id, "migrations", steps.MigrateAsync)
            && await Step(row.Id, "grants", steps.GrantAsync)
            && await Step(row.Id, "role-check", steps.CheckRuntimeRoleAsync)
            && await Step(row.Id, "version", () => RecordVersionAsync(row.Id, steps));
    }

    // A context is not thread safe, so each tenant writes its version through its own.
    private async Task RecordVersionAsync(string id, TenantDatabaseSteps steps)
    {
        var version = await steps.ReadSchemaVersionAsync();
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
        var tenant = await platform.Tenants.SingleAsync(row => row.Id == id, cancellationToken);
        tenant.SchemaVersion = version;
        tenant.Version++;

        // A tenant that this build serves has its keys, so the save that records the version also makes any that are missing.
        await TenantKeyRows.AddMissingAsync(platform, id, settings.Cipher, cancellationToken);
        await platform.SaveChangesAsync(cancellationToken);
    }

    // Runs one step. Any failure, also a stop by Ctrl+C, writes a failed line and ends that tenant.
    private async Task<bool> Step(string id, string name, Func<Task> work)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await work();
        }
        catch (Exception exception)
        {
            _output.WriteLine($"{id} {name} failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }

        _output.WriteLine($"{id} {name} ok");
        return true;
    }
}
