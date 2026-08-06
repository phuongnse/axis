using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListWorkspaceTransitionCleanupItems;

public sealed class ListWorkspaceTransitionCleanupItemsHandler(
    IWorkspaceTransitionCleanupStore cleanupStore)
    : IQueryHandler<ListWorkspaceTransitionCleanupItemsQuery, IReadOnlyList<WorkspaceTransitionCleanupItemDto>>
{
    public async Task<IReadOnlyList<WorkspaceTransitionCleanupItemDto>> Handle(
        ListWorkspaceTransitionCleanupItemsQuery query,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.BatchSize);

        IReadOnlyList<WorkspaceTransitionCleanupItem> items =
            await cleanupStore.ListTerminalWithoutRedisCleanupAsync(query.BatchSize, ct);

        return items.Select(item => new WorkspaceTransitionCleanupItemDto(
                item.TransitionId,
                item.SourceCorrelationDigest,
                item.TargetCorrelationDigest,
                item.Status == WorkspaceContextTransitionStatus.Completed,
                item.ExpiresAt))
            .ToArray();
    }
}
