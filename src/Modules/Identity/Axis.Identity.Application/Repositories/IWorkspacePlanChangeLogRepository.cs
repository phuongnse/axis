namespace Axis.Identity.Application.Repositories;

public interface IWorkspacePlanChangeLogRepository
{
    Task AddAsync(
        Guid workspaceId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default);
}
