using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;

namespace Axis.Identity.Application.Queries.GetWorkspaceSettings;

public sealed class GetWorkspaceSettingsHandler(
    IWorkspaceRepository workspaceRepo,
    ISubscriptionPlanRepository planRepo,
    IPlanLimitService planLimitService)
    : IQueryHandler<GetWorkspaceSettingsQuery, WorkspaceSettingsDto?>
{
    public async Task<WorkspaceSettingsDto?> Handle(
        GetWorkspaceSettingsQuery query,
        CancellationToken cancellationToken)
    {
        Workspace? Workspace = await workspaceRepo.GetByIdAsync(query.workspaceId, cancellationToken);
        if (Workspace is null)
            return null;

        SubscriptionPlan? plan =
            await planRepo.GetByIdAsync(Workspace.SubscriptionPlanId, cancellationToken);
        string planName = plan?.Name ?? "Unknown";

        PlanLimitUsageSnapshot? usage =
            await planLimitService.GetUsageSnapshotAsync(query.workspaceId, cancellationToken);

        UsageStatsDto usageDto = usage is null
            ? new UsageStatsDto(0, null, 0, null, 0, null)
            : new UsageStatsDto(
                usage.WorkflowsUsed,
                usage.WorkflowsLimit,
                usage.ExecutionsUsedThisMonth,
                usage.ExecutionsPerMonthLimit,
                usage.UsersUsed,
                usage.UsersLimit);

        return new WorkspaceSettingsDto(
            Workspace.Id,
            Workspace.Name,
            Workspace.Slug.Value,
            Workspace.LogoUrl,
            planName,
            Workspace.Status.ToString(),
            Workspace.CreatedAt,
            Workspace.TimeZoneId,
            Workspace.DefaultLanguage,
            Workspace.ScheduledHardDeleteAt,
            usageDto);
    }
}
