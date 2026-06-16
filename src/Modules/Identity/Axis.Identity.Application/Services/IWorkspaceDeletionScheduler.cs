namespace Axis.Identity.Application.Services;

public interface IWorkspaceDeletionScheduler
{
    Task ScheduleHardDeleteAsync(Guid workspaceId, DateTime hardDeleteAtUtc, CancellationToken ct = default);
}
