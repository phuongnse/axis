using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.PlanLimits;

public sealed class PlanLimitService(
    IOrganizationRepository organizationRepository,
    ISubscriptionPlanRepository subscriptionPlanRepository,
    IEnumerable<IPlanLimitUsageCounter> usageCounters,
    PlanLimitRedisCache redisCache,
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

        int current = await GetCurrentUsageAsync(organizationId, resourceType, cancellationToken);
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
        redisCache.TryInvalidateOrganizationAsync(organizationId, cancellationToken);

    public Task RecordUsageDeltaAsync(
        Guid organizationId,
        PlanLimitResourceType resourceType,
        int delta,
        CancellationToken cancellationToken = default)
    {
        if (delta == 0)
            return Task.CompletedTask;

        return redisCache.TryAdjustUsageAsync(organizationId, resourceType, delta, cancellationToken);
    }

    public async Task<PlanLimitUsageSnapshot?> GetUsageSnapshotAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        Organization? organization =
            await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return null;

        SubscriptionPlan? plan =
            await subscriptionPlanRepository.GetByIdAsync(organization.SubscriptionPlanId, cancellationToken);
        if (plan is null)
            return null;

        int workflowsUsed = await GetCurrentUsageAsync(organizationId, PlanLimitResourceType.Workflows, cancellationToken);
        int executionsUsed =
            await GetCurrentUsageAsync(organizationId, PlanLimitResourceType.ExecutionsPerMonth, cancellationToken);
        int usersUsed = await GetCurrentUsageAsync(organizationId, PlanLimitResourceType.Users, cancellationToken);

        return new PlanLimitUsageSnapshot(
            workflowsUsed,
            plan.MaxWorkflows,
            executionsUsed,
            plan.MaxExecutionsPerMonth,
            usersUsed,
            plan.MaxUsers);
    }

    private async Task<int> GetCurrentUsageAsync(
        Guid organizationId,
        PlanLimitResourceType resourceType,
        CancellationToken cancellationToken)
    {
        long? cached = await redisCache.TryGetCachedUsageAsync(organizationId, resourceType, cancellationToken);
        if (cached is not null)
        {
            if (cached.Value >= int.MaxValue)
                return int.MaxValue;
            if (cached.Value <= 0)
            {
                logger.LogWarning(
                    "Cached plan-limit usage is non-positive for org {OrganizationId} resource {ResourceType}: {CachedUsage}",
                    organizationId,
                    resourceType,
                    cached.Value);
                return 0;
            }

            return (int)cached.Value;
        }

        IPlanLimitUsageCounter? counter = usageCounters.FirstOrDefault(c => c.ResourceType == resourceType);
        if (counter is null)
        {
            logger.LogWarning(
                "No plan-limit usage counter registered for {ResourceType}; treating usage as zero.",
                resourceType);
            return 0;
        }

        int usage;
        try
        {
            usage = await counter.GetCurrentUsageAsync(organizationId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Plan limit usage counter failed for org {OrganizationId} resource {ResourceType}; treating usage as zero.",
                organizationId,
                resourceType);
            return 0;
        }

        bool cachedWrite = await redisCache.TrySetCachedUsageAsync(
            organizationId,
            resourceType,
            usage,
            cancellationToken);
        if (!cachedWrite)
        {
            logger.LogWarning(
                "Redis unavailable for plan limit cache (org {OrganizationId}, {ResourceType}); using database count {Usage}.",
                organizationId,
                resourceType,
                usage);
        }

        return usage;
    }

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
