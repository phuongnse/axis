using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TeamAccountPlanChangeLogRepository(IdentityDbContext context)
    : ITeamAccountPlanChangeLogRepository
{
    public async Task AddAsync(
        Guid teamAccountId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default)
    {
        TeamAccountPlanChangeLog entry = new TeamAccountPlanChangeLog
        {
            Id = Guid.NewGuid(),
            TeamAccountId = teamAccountId,
            PreviousPlanId = previousPlanId,
            NewPlanId = newPlanId,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow,
        };
        await context.Set<TeamAccountPlanChangeLog>().AddAsync(entry, ct);
    }
}
