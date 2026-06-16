namespace Axis.Identity.Application.Services;

public interface ITeamAccountDeletionScheduler
{
    Task ScheduleHardDeleteAsync(Guid teamAccountId, DateTime hardDeleteAtUtc, CancellationToken ct = default);
}
