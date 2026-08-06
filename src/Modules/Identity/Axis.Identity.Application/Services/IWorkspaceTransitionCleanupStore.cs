using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Services;

public interface IWorkspaceTransitionCleanupStore
{
    Task<IReadOnlyList<WorkspaceTransitionCleanupItem>> ListTerminalWithoutRedisCleanupAsync(
        int batchSize,
        CancellationToken ct = default);

    Task<bool> MarkRedisCleanupCompletedAsync(
        Guid transitionId,
        DateTimeOffset now,
        CancellationToken ct = default);
}

public sealed record WorkspaceTransitionCleanupItem(
    Guid TransitionId,
    string SourceCorrelationDigest,
    string TargetCorrelationDigest,
    WorkspaceContextTransitionStatus Status,
    DateTimeOffset ExpiresAt);
