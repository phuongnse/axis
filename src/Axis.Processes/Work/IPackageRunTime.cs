namespace Axis.Processes.Work;

/// <summary>
/// Counts the CPU seconds that the runner of a tenant's application package used, as the tenant's package run time of the day, see
/// docs/reference/tenant-quotas.md#package-run-time-a-day. The handler of package calls calls <see cref="AddAsync"/> once for each call that went to such a
/// runner, whatever the outcome. Before it marks a call to an application's package <c>sending</c>, it asks <see cref="CheckAsync"/>
/// whether the tenant's <c>packageCpuSecondsPerDay</c> quota holds the call. Only a cell's worker has one, so outside a cell nothing is held.
/// The first held call of a day takes the tenant's package-held notice, see <see cref="IHeldNotices"/>.
/// </summary>
public interface IPackageRunTime : IHeldNotices
{
    /// <summary>Adds <paramref name="cpuSeconds"/> to the tenant's package run time of today.</summary>
    Task AddAsync(string tenantId, long cpuSeconds, CancellationToken cancellationToken);

    /// <summary>
    /// Compares the tenant's package run time of the current UTC day with its quota. <see cref="PackageRunTimeCheck.Held"/> is
    /// <see langword="true"/> when the day's seconds are at or past the limit. Nothing is reserved. The result names the day,
    /// which the caller uses to find the next 00:00 UTC.
    /// </summary>
    Task<PackageRunTimeCheck> CheckAsync(string tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// The result of <see cref="IPackageRunTime.CheckAsync"/>: the UTC day it checked, whether the quota holds the call, and the
/// tenant's <c>packageCpuSecondsPerDay</c> limit, or <see langword="null"/> when it has none.
/// </summary>
public readonly record struct PackageRunTimeCheck(DateOnly Day, bool Held, long? Limit);
