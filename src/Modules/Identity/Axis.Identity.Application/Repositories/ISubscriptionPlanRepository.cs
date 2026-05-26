using Axis.Identity.Domain.Subscriptions;

namespace Axis.Identity.Application.Repositories;

public interface ISubscriptionPlanRepository
{
    Task<SubscriptionPlan?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlan>> ListActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailableForNewSignupsAsync(CancellationToken ct = default);
}
