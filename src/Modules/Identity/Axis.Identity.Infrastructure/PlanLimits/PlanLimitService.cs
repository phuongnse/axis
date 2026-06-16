using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.PlanLimits;

public sealed class PlanLimitService(
    ITeamAccountRepository teamAccountRepository,
    ISubscriptionPlanRepository subscriptionPlanRepository,
    IEnumerable<IPlanLimitUsageCounter> usageCounters,
    PlanLimitRedisCache redisCache,
    IConfiguration configuration,
    ILogger<PlanLimitService> logger) : IPlanLimitService
{
    public async Task<Result> EnsureWithinLimitAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        int increment = 1,
        CancellationToken cancellationToken = default)
    {
        TeamAccount? teamAccount =
            await teamAccountRepository.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null)
            return Result.Failure(ErrorCodes.NotFound, "Team account not found.");

        SubscriptionPlan? plan =
            await subscriptionPlanRepository.GetByIdAsync(teamAccount.SubscriptionPlanId, cancellationToken);
        if (plan is null)
            return Result.Failure(ErrorCodes.BusinessRule, "TeamAccount subscription plan is not configured.");

        int? limit = GetLimit(plan, resourceType);
        if (!plan.HasLimit(limit))
            return Result.Success();

        int current = await GetCurrentUsageAsync(teamAccountId, resourceType, cancellationToken);
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

    public Task RefreshCachedLimitsAsync(Guid teamAccountId, CancellationToken cancellationToken = default) =>
        redisCache.TryInvalidateTeamAccountAsync(teamAccountId, cancellationToken);

    public Task RecordUsageDeltaAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        int delta,
        CancellationToken cancellationToken = default)
    {
        if (delta == 0)
            return Task.CompletedTask;

        return redisCache.TryAdjustUsageAsync(teamAccountId, resourceType, delta, cancellationToken);
    }

    public async Task<PlanLimitUsageSnapshot?> GetUsageSnapshotAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken = default)
    {
        TeamAccount? teamAccount =
            await teamAccountRepository.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null)
            return null;

        SubscriptionPlan? plan =
            await subscriptionPlanRepository.GetByIdAsync(teamAccount.SubscriptionPlanId, cancellationToken);
        if (plan is null)
            return null;

        int workflowsUsed = await GetCurrentUsageAsync(teamAccountId, PlanLimitResourceType.Workflows, cancellationToken);
        int executionsUsed =
            await GetCurrentUsageAsync(teamAccountId, PlanLimitResourceType.ExecutionsPerMonth, cancellationToken);
        int usersUsed = await GetCurrentUsageAsync(teamAccountId, PlanLimitResourceType.Users, cancellationToken);

        return new PlanLimitUsageSnapshot(
            workflowsUsed,
            plan.MaxWorkflows,
            executionsUsed,
            plan.MaxExecutionsPerMonth,
            usersUsed,
            plan.MaxUsers);
    }

    private async Task<int> GetCurrentUsageAsync(
        Guid teamAccountId,
        PlanLimitResourceType resourceType,
        CancellationToken cancellationToken)
    {
        long? cached = await redisCache.TryGetCachedUsageAsync(teamAccountId, resourceType, cancellationToken);
        if (cached is not null)
        {
            if (cached.Value >= int.MaxValue)
                return int.MaxValue;
            if (cached.Value <= 0)
            {
                logger.LogWarning(
                    "Cached plan-limit usage is non-positive for team account {TeamAccountId} resource {ResourceType}: {CachedUsage}",
                    teamAccountId,
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
            usage = await counter.GetCurrentUsageAsync(teamAccountId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Plan limit usage counter failed for team account {TeamAccountId} resource {ResourceType}; treating usage as zero.",
                teamAccountId,
                resourceType);
            return 0;
        }

        bool cachedWrite = await redisCache.TrySetCachedUsageAsync(
            teamAccountId,
            resourceType,
            usage,
            cancellationToken);
        if (!cachedWrite)
        {
            logger.LogWarning(
                "Redis unavailable for plan limit cache (team account {TeamAccountId}, {ResourceType}); using database count {Usage}.",
                teamAccountId,
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
