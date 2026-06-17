using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspacePlanChangeLogRepository(IdentityDbContext context)
    : IWorkspacePlanChangeLogRepository
{
    public async Task AddAsync(
        Guid workspaceId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default)
    {
        WorkspacePlanChangeLog entry = new WorkspacePlanChangeLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            PreviousPlanId = previousPlanId,
            NewPlanId = newPlanId,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow,
        };
        await context.Set<WorkspacePlanChangeLog>().AddAsync(entry, ct);
    }
}
