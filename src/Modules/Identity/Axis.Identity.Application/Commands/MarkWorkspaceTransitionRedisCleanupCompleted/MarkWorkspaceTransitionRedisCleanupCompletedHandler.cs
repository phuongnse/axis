using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.MarkWorkspaceTransitionRedisCleanupCompleted;

public sealed class MarkWorkspaceTransitionRedisCleanupCompletedHandler(
    IWorkspaceTransitionCleanupStore cleanupStore)
    : ICommandHandler<MarkWorkspaceTransitionRedisCleanupCompletedCommand, bool>
{
    public async Task<Result<bool>> Handle(
        MarkWorkspaceTransitionRedisCleanupCompletedCommand command,
        CancellationToken ct) =>
        Result.Success(await cleanupStore.MarkRedisCleanupCompletedAsync(
            command.TransitionId,
            command.Now,
            ct));
}
