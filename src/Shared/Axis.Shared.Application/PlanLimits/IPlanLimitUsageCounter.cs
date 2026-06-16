namespace Axis.Shared.Application.PlanLimits;

/// <summary>Module-specific usage counter for plan enforcement (wired at API composition root).</summary>
public interface IPlanLimitUsageCounter
{
    PlanLimitResourceType ResourceType { get; }

    Task<int> GetCurrentUsageAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
