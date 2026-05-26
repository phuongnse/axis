namespace Axis.Identity.Application.Repositories;

public interface IOrganizationPlanChangeLogRepository
{
    Task AddAsync(
        Guid organizationId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default);
}
