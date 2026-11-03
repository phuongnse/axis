namespace Axis.Processes.Work;

/// <summary>
/// Counts the work items a runner ran, as a tenant's usage. The runner calls it once for each item that ended
/// as completed, failed or rescheduled, right after the transaction of the item in the tenant database ended.
/// </summary>
public interface IWorkItemUsage
{
    /// <summary>Counts one run of an item of <paramref name="kind"/> for the tenant.</summary>
    Task CountAsync(string tenantId, string kind, CancellationToken cancellationToken);
}
