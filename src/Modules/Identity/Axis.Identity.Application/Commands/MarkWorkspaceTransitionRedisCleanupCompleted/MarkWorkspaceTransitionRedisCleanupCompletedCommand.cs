using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.MarkWorkspaceTransitionRedisCleanupCompleted;

public sealed record MarkWorkspaceTransitionRedisCleanupCompletedCommand(
    Guid TransitionId,
    DateTimeOffset Now) : ICommand<bool>;
