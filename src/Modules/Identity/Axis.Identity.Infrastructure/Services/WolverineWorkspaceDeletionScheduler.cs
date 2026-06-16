using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Wolverine;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WolverineWorkspaceDeletionScheduler(IMessageBus messageBus) : IWorkspaceDeletionScheduler
{
    public async Task ScheduleHardDeleteAsync(Guid workspaceId, DateTime hardDeleteAtUtc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await messageBus.ScheduleAsync(
            new WorkspaceHardDeleteJob(workspaceId),
            new DateTimeOffset(hardDeleteAtUtc, TimeSpan.Zero));
    }
}
