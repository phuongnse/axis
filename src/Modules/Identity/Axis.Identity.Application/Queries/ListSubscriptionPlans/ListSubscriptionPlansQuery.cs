using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListSubscriptionPlans;

public sealed record ListSubscriptionPlansQuery(Guid? CurrentTenantId) : IQuery<IReadOnlyList<SubscriptionPlanDto>>;
