namespace Axis.Identity.Application.Services;

public interface ITenantDeletionScheduler
{
    Task ScheduleHardDeleteAsync(Guid tenantId, DateTime hardDeleteAtUtc, CancellationToken ct = default);
}
