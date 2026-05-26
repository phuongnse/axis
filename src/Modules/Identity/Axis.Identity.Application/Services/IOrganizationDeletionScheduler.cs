namespace Axis.Identity.Application.Services;

public interface IOrganizationDeletionScheduler
{
    Task ScheduleHardDeleteAsync(Guid organizationId, DateTime hardDeleteAtUtc, CancellationToken ct = default);
}
