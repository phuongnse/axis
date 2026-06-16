using Axis.Shared.Domain.Primitives;

namespace Axis.Shared.Application.PlanLimits;

public interface IPlanLimitService
{
    /// <summary>
    /// Ensures the Workspace can add <paramref name="increment"/> resources of the given type.
    /// Returns a plan_limit Result with structured plan-limit details when blocked.
    /// </summary>
    Task<Result> EnsureWithinLimitAsync(
        Guid workspaceId,
        PlanLimitResourceType resourceType,
        int increment = 1,
        CancellationToken cancellationToken = default);

    Task RefreshCachedLimitsAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Adjusts Redis usage counters after a successful mutation.</summary>
    Task RecordUsageDeltaAsync(
        Guid workspaceId,
        PlanLimitResourceType resourceType,
        int delta,
        CancellationToken cancellationToken = default);

    /// <summary>Current usage vs plan limits for Workspace settings.</summary>
    Task<PlanLimitUsageSnapshot?> GetUsageSnapshotAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}
