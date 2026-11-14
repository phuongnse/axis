namespace Axis.Processes.Work;

/// <summary>
/// Lets only the first held work item of a tenant in a UTC day store the notice for the tenant's administrators. A quota's
/// hold takes the day's notice counter, with a limit of 1, in the transaction that holds the item, see
/// docs/reference/tenant-quotas.md#emails-a-day.
/// </summary>
public interface IHeldNotices
{
    /// <summary>
    /// Takes the one held-notice of the tenant on <paramref name="day"/>. Returns <see langword="false"/> when
    /// the day's notice is taken already, so a later hold stores no second one.
    /// </summary>
    Task<bool> TakeHeldNoticeAsync(string tenantId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Gives back the notice taken on <paramref name="day"/>, because the transaction that stores it did not commit.</summary>
    Task ReturnHeldNoticeAsync(string tenantId, DateOnly day, CancellationToken cancellationToken);
}
