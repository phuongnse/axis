using System.Collections.Concurrent;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// What the health endpoints of the worker report. The loop says when it polled, when a tenant
/// database counted as migrated and when a tenant waits for its schema version. A waiting tenant counts as ready,
/// so one tenant whose migration failed does not keep the worker from being ready. The loop is stalled when it has not polled within twice the lease,
/// because one work item can run for up to a lease. Before the first poll the time counts from the start.
/// </summary>
internal sealed class WorkerHealth(TimeProvider time, WorkerOptions options, ITenantCatalog tenants)
{
    private long _lastPolledTicks = time.GetUtcNow().UtcTicks;
    private readonly ConcurrentDictionary<string, byte> _migrated = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _waiting = new(StringComparer.Ordinal);

    public bool LoopStalled =>
        time.GetUtcNow() - new DateTimeOffset(Interlocked.Read(ref _lastPolledTicks), TimeSpan.Zero) > options.LeaseDuration * 2;

    public bool AllTenantsMigrated =>
        tenants.Tenants.All(tenant => _migrated.ContainsKey(tenant.Id) || _waiting.ContainsKey(tenant.Id));

    public void Polled() => Interlocked.Exchange(ref _lastPolledTicks, time.GetUtcNow().UtcTicks);

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
}
