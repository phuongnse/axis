namespace Axis.Identity.Application.Repositories;

public interface ITeamAccountPlanChangeLogRepository
{
    Task AddAsync(
        Guid teamAccountId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default);
}
