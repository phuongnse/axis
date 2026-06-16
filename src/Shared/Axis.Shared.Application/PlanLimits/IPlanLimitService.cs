using Axis.Shared.Domain.Primitives;

namespace Axis.Shared.Application.PlanLimits;

public interface IPlanLimitService
{
    /// <summary>
    /// Ensures the team account can add <paramref name="increment"/> resources of the given type.
    /// Returns a plan_limit Result with structured plan-limit details when blocked.
    /// </summary>
    Task<Result> EnsureWithinLimitAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        int increment = 1,
        CancellationToken cancellationToken = default);

    Task RefreshCachedLimitsAsync(Guid teamAccountId, CancellationToken cancellationToken = default);

    /// <summary>Adjusts Redis usage counters after a successful mutation.</summary>
    Task RecordUsageDeltaAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        int delta,
        CancellationToken cancellationToken = default);

    /// <summary>Current usage vs plan limits for team account settings.</summary>
    Task<PlanLimitUsageSnapshot?> GetUsageSnapshotAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken = default);
}
