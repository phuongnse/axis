using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TenantPlanChangeLogRepository(IdentityDbContext context)
    : ITenantPlanChangeLogRepository
{
    public async Task AddAsync(
        Guid tenantId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default)
    {
        TenantPlanChangeLog entry = new TenantPlanChangeLog
        {
            Id = Guid.NewGuid(),
            tenantId = tenantId,
            PreviousPlanId = previousPlanId,
            NewPlanId = newPlanId,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow,
        };
        await context.Set<TenantPlanChangeLog>().AddAsync(entry, ct);
    }
}
