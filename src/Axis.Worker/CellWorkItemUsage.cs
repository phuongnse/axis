using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Tenancy.Platform;

namespace Axis.Worker;

/// <summary>
/// Counts the work items of a cell's worker in the tenant's row of the day. A connector call or a package call
/// counts as a call too, and every attempt counts, also one that the handler reschedules.
/// </summary>
public sealed class CellWorkItemUsage(TenantUsageStore usage, TimeProvider time) : IWorkItemUsage
{
    public Task CountAsync(string tenantId, string kind, CancellationToken cancellationToken) =>
        usage.CountWorkItemAsync(
            tenantId,
            kind is ProcessStarts.ConnectorCallWorkItemKind or ProcessStarts.PackageCallWorkItemKind,
            time.GetUtcNow(),
            cancellationToken);
}
