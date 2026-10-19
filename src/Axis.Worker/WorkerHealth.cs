using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// What the health endpoints of the worker report. The loop says when it polled and when a tenant
/// database counted as migrated. The loop is stalled when it has not polled within twice the lease,
/// because one work item can run for up to a lease. Before the first poll the time counts from the start.
/// </summary>
internal sealed class WorkerHealth(TimeProvider time, WorkerOptions options, ITenantCatalog tenants)
{
    private long _lastPolledTicks = time.GetUtcNow().UtcTicks;
    private int _migratedTenants;

    public bool LoopStalled =>
        time.GetUtcNow() - new DateTimeOffset(Interlocked.Read(ref _lastPolledTicks), TimeSpan.Zero) > options.LeaseDuration * 2;

    public bool AllTenantsMigrated => Volatile.Read(ref _migratedTenants) >= tenants.Tenants.Count;

    public void Polled() => Interlocked.Exchange(ref _lastPolledTicks, time.GetUtcNow().UtcTicks);

    public void TenantMigrated() => Interlocked.Increment(ref _migratedTenants);
}
