using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListWorkspaceTransitionCleanupItems;

public sealed record ListWorkspaceTransitionCleanupItemsQuery(int BatchSize)
    : IQuery<IReadOnlyList<WorkspaceTransitionCleanupItemDto>>;

public sealed record WorkspaceTransitionCleanupItemDto(
    Guid TransitionId,
    string SourceCorrelationDigest,
    string TargetCorrelationDigest,
    bool IsCompleted,
    DateTimeOffset ExpiresAt);
