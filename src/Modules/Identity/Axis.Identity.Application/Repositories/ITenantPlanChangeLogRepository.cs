namespace Axis.Identity.Application.Repositories;

public interface ITenantPlanChangeLogRepository
{
    Task AddAsync(
        Guid tenantId,
        Guid previousPlanId,
        Guid newPlanId,
        Guid changedByUserId,
        CancellationToken ct = default);
}
