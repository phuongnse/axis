using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Worker;

/// <summary>
/// Counts the CPU seconds of the application package runners of a cell's worker in the tenant's <c>packageCpuSeconds</c> counter of the
/// UTC day, and holds a call when that counter is at or past the tenant's <c>packageCpuSecondsPerDay</c> quota, see
/// docs/reference/tenant-quotas.md#package-run-time-a-day. The count has no limit, so it stays right when a call that started under the quota
/// ends past it, and a tenant with no limit is counted too. The worker's clock decides the day, as it does for <see cref="CellEmailQuota"/>.
/// The tenant's <c>packageHeldNotice</c> counter of the day, with a limit of 1, lets only the first held call of the day store a notice.
/// </summary>
public sealed class CellPackageRunTime(TenantDailyCounterStore counters, TenantQuotas quotas, TimeProvider time) : IPackageRunTime
{
    public Task AddAsync(string tenantId, long cpuSeconds, CancellationToken cancellationToken) =>
        counters.AddAsync(tenantId, TenantDailyCounterStore.PackageCpuSeconds, Today(), cpuSeconds, cancellationToken);

    public async Task<PackageRunTimeCheck> CheckAsync(string tenantId, CancellationToken cancellationToken)
    {
        var day = Today();
        if (quotas.Limit(tenantId, QuotaKeys.PackageCpuSecondsPerDay) is not { } limit)
        {
            return new PackageRunTimeCheck(day, Held: false, Limit: null);
        }

        var used = await counters.ValueAsync(tenantId, TenantDailyCounterStore.PackageCpuSeconds, day, cancellationToken);
        return new PackageRunTimeCheck(day, used >= limit, limit);
    }

    public Task<bool> TakeHeldNoticeAsync(string tenantId, DateOnly day, CancellationToken cancellationToken) =>
        counters.TryIncrementAsync(tenantId, TenantDailyCounterStore.PackageHeldNotice, day, 1, cancellationToken);

    public Task ReturnHeldNoticeAsync(string tenantId, DateOnly day, CancellationToken cancellationToken) =>
        counters.DecrementAsync(tenantId, TenantDailyCounterStore.PackageHeldNotice, day, cancellationToken);

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
