using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Worker;

/// <summary>
/// Takes the <c>concurrentConnectorCalls</c> lease of a tenant for a connector call or a package call in a cell's worker,
/// through <see cref="TenantLeases"/>, see <c>docs/reference/tenant-quotas.md#work-at-once</c>. It never waits: the claim
/// leaves the work item due when no lease is free. A tenant with no limit gets a lease that holds nothing, and the platform
/// database is not read. When the platform database cannot be reached the call goes ahead without a lease and the worker
/// logs a warning, as an import does.
/// </summary>
public sealed partial class CellCallLeases(TenantLeases leases, TenantQuotas quotas, ILogger<CellCallLeases> logger) : ICallLeases
{
    public async Task<IAsyncDisposable?> TryTakeAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (quotas.Limit(tenantId, QuotaKeys.ConcurrentConnectorCalls) is not { } limit)
        {
            return NoLease.Instance;
        }

        try
        {
            return await leases.TryTakeAsync(tenantId, QuotaKeys.ConcurrentConnectorCalls, limit, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            LogUnavailable(tenantId, exception.GetType().Name);
            return NoLease.Instance;
        }
    }

    [LoggerMessage(LogLevel.Warning, "A concurrentConnectorCalls lease of tenant {TenantId} could not be taken over ConnectionStrings:Platform ({Cause}). The call runs without one.")]
    private partial void LogUnavailable(string tenantId, string cause);

    private sealed class NoLease : IAsyncDisposable
    {
        public static readonly NoLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
