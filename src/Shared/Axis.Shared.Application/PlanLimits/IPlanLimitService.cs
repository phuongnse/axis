using Axis.Shared.Domain.Primitives;

namespace Axis.Shared.Application.PlanLimits;

public interface IPlanLimitService
{
    /// <summary>
    /// Ensures the organization can add <paramref name="increment"/> resources of the given type.
    /// Returns a plan_limit Result with structured plan-limit details when blocked.
    /// </summary>
    Task<Result> EnsureWithinLimitAsync(
        Guid organizationId,
        PlanLimitResourceType resourceType,
        int increment = 1,
        CancellationToken cancellationToken = default);

    Task RefreshCachedLimitsAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Adjusts Redis usage counters after a successful mutation (US-011 edge: decrement on delete).</summary>
    Task RecordUsageDeltaAsync(
        Guid organizationId,
        PlanLimitResourceType resourceType,
        int delta,
        CancellationToken cancellationToken = default);
}
