namespace Axis.Processes.Work;

/// <summary>
/// Counts the CPU seconds that the runner of a tenant's application package used, as the tenant's package run time of the day, see
/// docs/reference/tenant-quotas.md#package-run-time-a-day. The handler of package calls calls it once for each call that went to such a
/// runner, whatever the outcome.
/// </summary>
public interface IPackageRunTime
{
    /// <summary>Adds <paramref name="cpuSeconds"/> to the tenant's package run time of today.</summary>
    Task AddAsync(string tenantId, long cpuSeconds, CancellationToken cancellationToken);
}
