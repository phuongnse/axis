using System.Collections.Concurrent;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// What the health endpoints of the worker report. The poll says when it ran, when a tenant
/// database counted as migrated, when a tenant waits for its schema version and when a tenant left the directory. A waiting tenant counts as ready,
/// so one tenant whose migration failed does not keep the worker from being ready. Each loop says when it starts and ends a work item.
/// The worker is stalled when the poll has not run within twice the lease, or when one loop has been on the same work item for longer than that,
/// because one work item can run for up to a lease. Before the first poll the time counts from the start.
/// </summary>
internal sealed class WorkerHealth(TimeProvider time, WorkerOptions options, ITenantCatalog tenants)
{
    private long _lastPolledTicks = time.GetUtcNow().UtcTicks;
    private readonly ConcurrentDictionary<string, byte> _migrated = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _waiting = new(StringComparer.Ordinal);

    // The start ticks of the work item each loop is running, keyed by loop.
    private readonly ConcurrentDictionary<int, long> _itemStartedTicks = new();

    public bool LoopStalled
    {
        get
        {
            var now = time.GetUtcNow();
            var limit = options.LeaseDuration * 2;
            return now - new DateTimeOffset(Interlocked.Read(ref _lastPolledTicks), TimeSpan.Zero) > limit
                || _itemStartedTicks.Values.Any(started => now - new DateTimeOffset(started, TimeSpan.Zero) > limit);
        }
    }

    public bool AllTenantsMigrated =>
        tenants.Tenants.All(tenant => _migrated.ContainsKey(tenant.Id) || _waiting.ContainsKey(tenant.Id));

    public void Polled() => Interlocked.Exchange(ref _lastPolledTicks, time.GetUtcNow().UtcTicks);

    /// <summary>Marks that the loop began a work item.</summary>
    public void ItemStarted(int loop) => _itemStartedTicks[loop] = time.GetUtcNow().UtcTicks;

    /// <summary>Marks that the loop ended its work item, whatever the outcome.</summary>
    public void ItemFinished(int loop) => _itemStartedTicks.TryRemove(loop, out _);

    public void TenantMigrated(string tenantId) => _migrated[tenantId] = 0;

    /// <summary>Marks a tenant that is on another schema version than the worker's, or clears the mark.</summary>
    public void SchemaWaiting(string tenantId, bool waiting)
    {
        if (waiting)
        {
            _waiting[tenantId] = 0;
        }
        else
        {
            _waiting.TryRemove(tenantId, out _);
        }
    }

    public void TenantDropped(string tenantId)
    {
        _migrated.TryRemove(tenantId, out _);
        _waiting.TryRemove(tenantId, out _);
    }
}
