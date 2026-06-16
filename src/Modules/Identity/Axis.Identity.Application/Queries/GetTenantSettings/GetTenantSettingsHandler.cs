using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;

namespace Axis.Identity.Application.Queries.GetTenantSettings;

public sealed class GetTenantSettingsHandler(
    ITenantRepository tenantRepo,
    ISubscriptionPlanRepository planRepo,
    IPlanLimitService planLimitService)
    : IQueryHandler<GetTenantSettingsQuery, TenantSettingsDto?>
{
    public async Task<TenantSettingsDto?> Handle(
        GetTenantSettingsQuery query,
        CancellationToken cancellationToken)
    {
        Tenant? Tenant = await tenantRepo.GetByIdAsync(query.tenantId, cancellationToken);
        if (Tenant is null)
            return null;

        SubscriptionPlan? plan =
            await planRepo.GetByIdAsync(Tenant.SubscriptionPlanId, cancellationToken);
        string planName = plan?.Name ?? "Unknown";

        PlanLimitUsageSnapshot? usage =
            await planLimitService.GetUsageSnapshotAsync(query.tenantId, cancellationToken);

        UsageStatsDto usageDto = usage is null
            ? new UsageStatsDto(0, null, 0, null, 0, null)
            : new UsageStatsDto(
                usage.WorkflowsUsed,
                usage.WorkflowsLimit,
                usage.ExecutionsUsedThisMonth,
                usage.ExecutionsPerMonthLimit,
                usage.UsersUsed,
                usage.UsersLimit);

        return new TenantSettingsDto(
            Tenant.Id,
            Tenant.Name,
            Tenant.Slug.Value,
            Tenant.LogoUrl,
            planName,
            Tenant.Status.ToString(),
            Tenant.CreatedAt,
            Tenant.TimeZoneId,
            Tenant.DefaultLanguage,
            Tenant.ScheduledHardDeleteAt,
            usageDto);
    }
}
