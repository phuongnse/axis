using Axis.Processes.Work;
using Axis.Tenancy.Platform;

namespace Axis.Worker;

/// <summary>
/// Counts the CPU seconds of the application package runners of a cell's worker in the tenant's <c>packageCpuSeconds</c> counter of the
/// UTC day, with no limit, so the count stays right when a <c>packageCpuSecondsPerDay</c> quota starts to apply. The worker's clock
/// decides the day, as it does for <see cref="CellEmailQuota"/>.
/// </summary>
public sealed class CellPackageRunTime(TenantDailyCounterStore counters, TimeProvider time) : IPackageRunTime
{
    public Task AddAsync(string tenantId, long cpuSeconds, CancellationToken cancellationToken) =>
        counters.AddAsync(
            tenantId, TenantDailyCounterStore.PackageCpuSeconds, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), cpuSeconds, cancellationToken);
}
