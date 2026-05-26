using Axis.Identity.Application.Repositories;
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
        IReadOnlyList<SubscriptionPlan> plans = await planRepo.ListAvailableForNewSignupsAsync(cancellationToken);

        Guid? currentPlanId = null;
        if (query.CurrentOrganizationId is Guid orgId)
        {
            Domain.Aggregates.Organization? org = await orgRepo.GetByIdAsync(orgId, cancellationToken);
            currentPlanId = org?.SubscriptionPlanId;
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
            currentPlanId == plan.Id);
}
