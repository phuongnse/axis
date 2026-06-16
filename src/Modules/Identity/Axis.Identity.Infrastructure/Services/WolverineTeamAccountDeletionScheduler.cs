using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Wolverine;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WolverineTeamAccountDeletionScheduler(IMessageBus messageBus) : ITeamAccountDeletionScheduler
{
    public async Task ScheduleHardDeleteAsync(Guid teamAccountId, DateTime hardDeleteAtUtc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await messageBus.ScheduleAsync(
            new TeamAccountHardDeleteJob(teamAccountId),
            new DateTimeOffset(hardDeleteAtUtc, TimeSpan.Zero));
    }
}
