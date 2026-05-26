using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class SubscriptionPlanRepository(IdentityDbContext context) : ISubscriptionPlanRepository
{
    public Task<SubscriptionPlan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<SubscriptionPlan>> ListActiveAsync(CancellationToken ct = default) =>
        await context.SubscriptionPlans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPriceCents)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailableForNewSignupsAsync(CancellationToken ct = default) =>
        await context.SubscriptionPlans.AsNoTracking()
            .Where(p => p.IsActive && p.IsAvailableForNewSignups)
            .OrderBy(p => p.MonthlyPriceCents)
            .ToListAsync(ct);
}
