using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class OrganizationPlanChangeLogRepository(IdentityDbContext context)
    : IOrganizationPlanChangeLogRepository
{
    public async Task AddAsync(
        Guid organizationId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default)
    {
        OrganizationPlanChangeLog entry = new OrganizationPlanChangeLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PreviousPlanId = previousPlanId,
            NewPlanId = newPlanId,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow,
        };
        await context.Set<OrganizationPlanChangeLog>().AddAsync(entry, ct);
    }
}
