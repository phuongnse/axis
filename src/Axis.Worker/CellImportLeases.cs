using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Worker;

/// <summary>
/// Takes the <c>concurrentImports</c> lease of a tenant for the dry run or the commit of an import in a cell's worker,
/// through <see cref="TenantLeases"/>, see <c>docs/reference/tenant-quotas.md#work-at-once</c>. It never waits: the
/// import handler puts the work item back for a second when no lease is free. A tenant with no limit gets a lease that holds
/// nothing, and the platform database is not read. When the platform database cannot be reached the import goes ahead without
/// a lease and the worker logs a warning, as a read does.
/// </summary>
public sealed partial class CellImportLeases(TenantLeases leases, TenantQuotas quotas, ILogger<CellImportLeases> logger) : IImportLeases
{
    public async Task<IAsyncDisposable?> TryTakeAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (quotas.Limit(tenantId, QuotaKeys.ConcurrentImports) is not { } limit)
        {
            return NoLease.Instance;
        }

        try
        {
            return await leases.TryTakeAsync(tenantId, QuotaKeys.ConcurrentImports, limit, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            LogUnavailable(tenantId, exception.GetType().Name);
            return NoLease.Instance;
        }
    }

    [LoggerMessage(LogLevel.Warning, "A concurrentImports lease of tenant {TenantId} could not be taken over ConnectionStrings:Platform ({Cause}). The import runs without one.")]
    private partial void LogUnavailable(string tenantId, string cause);

    private sealed class NoLease : IAsyncDisposable
    {
        public static readonly NoLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
