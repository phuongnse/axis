using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListSubscriptionPlans;

public sealed class ListSubscriptionPlansHandler(
    ISubscriptionPlanRepository planRepo,
    IOrganizationRepository orgRepo)
    : IQueryHandler<ListSubscriptionPlansQuery, IReadOnlyList<SubscriptionPlanDto>>
{
    public async Task<IReadOnlyList<SubscriptionPlanDto>> Handle(
        ListSubscriptionPlansQuery query,
        CancellationToken cancellationToken)
    {
        List<SubscriptionPlan> plans = (await planRepo.ListAvailableForNewSignupsAsync(cancellationToken)).ToList();

        Guid? currentPlanId = null;
        if (query.CurrentOrganizationId is Guid orgId)
        {
            Organization? org = await orgRepo.GetByIdAsync(orgId, cancellationToken);
            currentPlanId = org?.SubscriptionPlanId;
        }

        // US-010 edge: retired plans stay visible to orgs still on that plan.
        if (currentPlanId is Guid planId && plans.All(p => p.Id != planId))
        {
            SubscriptionPlan? currentPlan = await planRepo.GetByIdAsync(planId, cancellationToken);
            if (currentPlan is not null && currentPlan.IsActive)
                plans.Add(currentPlan);
        }

        return plans.Select(plan => Map(plan, currentPlanId)).ToList();
    }

    private static SubscriptionPlanDto Map(SubscriptionPlan plan, Guid? currentPlanId) =>
        new(
            plan.Id,
            plan.Name,
            plan.Slug,
            plan.MonthlyPriceCents,
            plan.MaxWorkflows,
            plan.MaxExecutionsPerMonth,
            plan.MaxUsers,
            plan.MaxStorageMegabytes,
            SubscriptionPlanFeatureFlags.ForSlug(plan.Slug),
            currentPlanId == plan.Id,
            plan.IsAvailableForNewSignups);
}
