using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;

namespace Axis.Identity.Application.Queries.GetOrganizationSettings;

public sealed class GetOrganizationSettingsHandler(
    IOrganizationRepository orgRepo,
    ISubscriptionPlanRepository planRepo,
    IPlanLimitService planLimitService)
    : IQueryHandler<GetOrganizationSettingsQuery, OrganizationSettingsDto?>
{
    public async Task<OrganizationSettingsDto?> Handle(
        GetOrganizationSettingsQuery query,
        CancellationToken cancellationToken)
    {
        Organization? organization = await orgRepo.GetByIdAsync(query.OrganizationId, cancellationToken);
        if (organization is null)
            return null;

        SubscriptionPlan? plan =
            await planRepo.GetByIdAsync(organization.SubscriptionPlanId, cancellationToken);
        string planName = plan?.Name ?? "Unknown";

        PlanLimitUsageSnapshot? usage =
            await planLimitService.GetUsageSnapshotAsync(query.OrganizationId, cancellationToken);

        UsageStatsDto usageDto = usage is null
            ? new UsageStatsDto(0, null, 0, null, 0, null)
            : new UsageStatsDto(
                usage.WorkflowsUsed,
                usage.WorkflowsLimit,
                usage.ExecutionsUsedThisMonth,
                usage.ExecutionsPerMonthLimit,
                usage.UsersUsed,
                usage.UsersLimit);

        return new OrganizationSettingsDto(
            organization.Id,
            organization.Name,
            organization.Slug.Value,
            organization.LogoUrl,
            planName,
            organization.Status.ToString(),
            organization.CreatedAt,
            organization.TimeZoneId,
            organization.DefaultLanguage,
            organization.ScheduledHardDeleteAt,
            usageDto);
    }
}
