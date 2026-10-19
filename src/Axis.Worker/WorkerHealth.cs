using System.Collections.Concurrent;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// What the health endpoints of the worker report. The loop says when it polled and when a tenant
/// database counted as migrated, and when a tenant left the directory. The loop is stalled when it has not polled within twice the lease,
/// because one work item can run for up to a lease. Before the first poll the time counts from the start.
/// </summary>
internal sealed class WorkerHealth(TimeProvider time, WorkerOptions options, ITenantCatalog tenants)
{
    private long _lastPolledTicks = time.GetUtcNow().UtcTicks;
    private readonly ConcurrentDictionary<string, bool> _migratedTenants = new(StringComparer.Ordinal);

    public bool LoopStalled =>
        time.GetUtcNow() - new DateTimeOffset(Interlocked.Read(ref _lastPolledTicks), TimeSpan.Zero) > options.LeaseDuration * 2;

    public bool AllTenantsMigrated => tenants.Tenants.All(tenant => _migratedTenants.ContainsKey(tenant.Id));

    public void Polled() => Interlocked.Exchange(ref _lastPolledTicks, time.GetUtcNow().UtcTicks);

    public void TenantMigrated(string tenantId) => _migratedTenants[tenantId] = true;

    public void TenantDropped(string tenantId) => _migratedTenants.TryRemove(tenantId, out _);
}
