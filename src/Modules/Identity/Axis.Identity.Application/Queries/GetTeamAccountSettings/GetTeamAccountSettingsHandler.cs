using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;

namespace Axis.Identity.Application.Queries.GetTeamAccountSettings;

public sealed class GetTeamAccountSettingsHandler(
    ITeamAccountRepository teamAccountRepo,
    ISubscriptionPlanRepository planRepo,
    IPlanLimitService planLimitService)
    : IQueryHandler<GetTeamAccountSettingsQuery, TeamAccountSettingsDto?>
{
    public async Task<TeamAccountSettingsDto?> Handle(
        GetTeamAccountSettingsQuery query,
        CancellationToken cancellationToken)
    {
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(query.TeamAccountId, cancellationToken);
        if (teamAccount is null)
            return null;

        SubscriptionPlan? plan =
            await planRepo.GetByIdAsync(teamAccount.SubscriptionPlanId, cancellationToken);
        string planName = plan?.Name ?? "Unknown";

        PlanLimitUsageSnapshot? usage =
            await planLimitService.GetUsageSnapshotAsync(query.TeamAccountId, cancellationToken);

        UsageStatsDto usageDto = usage is null
            ? new UsageStatsDto(0, null, 0, null, 0, null)
            : new UsageStatsDto(
                usage.WorkflowsUsed,
                usage.WorkflowsLimit,
                usage.ExecutionsUsedThisMonth,
                usage.ExecutionsPerMonthLimit,
                usage.UsersUsed,
                usage.UsersLimit);

        return new TeamAccountSettingsDto(
            teamAccount.Id,
            teamAccount.Name,
            teamAccount.Slug.Value,
            teamAccount.LogoUrl,
            planName,
            teamAccount.Status.ToString(),
            teamAccount.CreatedAt,
            teamAccount.TimeZoneId,
            teamAccount.DefaultLanguage,
            teamAccount.ScheduledHardDeleteAt,
            usageDto);
    }
}
