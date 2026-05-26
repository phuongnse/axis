using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Api.Infrastructure.PlanLimits;

public sealed class PlanLimitService(
    IOrganizationRepository organizationRepository,
    ISubscriptionPlanRepository subscriptionPlanRepository,
    IEnumerable<IPlanLimitUsageCounter> usageCounters,
    IConfiguration configuration,
    ILogger<PlanLimitService> logger) : IPlanLimitService
{
    public async Task<Result> EnsureWithinLimitAsync(
        Guid organizationId,
        PlanLimitResourceType resourceType,
        int increment = 1,
        CancellationToken cancellationToken = default)
    {
        Organization? organization =
            await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        SubscriptionPlan? plan =
            await subscriptionPlanRepository.GetByIdAsync(organization.SubscriptionPlanId, cancellationToken);
        if (plan is null)
            return Result.Failure(ErrorCodes.BusinessRule, "Organization subscription plan is not configured.");

        int? limit = GetLimit(plan, resourceType);
        if (!plan.HasLimit(limit))
            return Result.Success();

        IPlanLimitUsageCounter? counter = usageCounters.FirstOrDefault(c => c.ResourceType == resourceType);
        if (counter is null)
        {
            logger.LogWarning(
                "No plan-limit usage counter registered for {ResourceType}; allowing operation.",
                resourceType);
            return Result.Success();
        }

        int current = await counter.GetCurrentUsageAsync(organizationId, cancellationToken);
        if (plan.IsWithinLimit(limit, current, increment))
            return Result.Success();

        string limitType = MapLimitType(resourceType);
        string upgradeUrl = configuration["Platform:UpgradeUrl"] ?? "/pricing";
        PlanLimitFailureDetails details = new(
            limitType,
            current,
            limit!.Value,
            upgradeUrl,
            BuildMessage(limitType, current, limit.Value));

        return Result.PlanLimitFailure(details);
    }

    public Task RefreshCachedLimitsAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private static int? GetLimit(SubscriptionPlan plan, PlanLimitResourceType resourceType) =>
        resourceType switch
        {
            PlanLimitResourceType.Workflows => plan.MaxWorkflows,
            PlanLimitResourceType.ExecutionsPerMonth => plan.MaxExecutionsPerMonth,
            PlanLimitResourceType.Users => plan.MaxUsers,
            _ => null,
        };

    private static string MapLimitType(PlanLimitResourceType resourceType) =>
        resourceType switch
        {
            PlanLimitResourceType.Workflows => "workflows",
            PlanLimitResourceType.ExecutionsPerMonth => "executions_per_month",
            PlanLimitResourceType.Users => "users",
            _ => resourceType.ToString(),
        };

    private static string BuildMessage(string limitType, int current, int max) =>
        limitType switch
        {
            "workflows" =>
                $"Your plan allows {max} workflows. You currently have {current}. Upgrade your plan to create more.",
            "executions_per_month" =>
                $"Your plan allows {max} executions per month. You have used {current}. Upgrade your plan for more capacity.",
            "users" =>
                $"Your plan allows {max} users. You currently have {current} users and pending invitations. Upgrade your plan to invite more.",
            _ => "Plan limit exceeded. Please upgrade your subscription.",
        };
}
