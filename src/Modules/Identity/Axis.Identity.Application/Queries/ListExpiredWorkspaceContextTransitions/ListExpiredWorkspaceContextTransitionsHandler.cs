using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListExpiredWorkspaceContextTransitions;

public sealed class ListExpiredWorkspaceContextTransitionsHandler(
    IWorkspaceTransitionExpiryStore expiryStore)
    : IQueryHandler<ListExpiredWorkspaceContextTransitionsQuery, IReadOnlyList<ExpiredWorkspaceContextTransitionDto>>
{
    public async Task<IReadOnlyList<ExpiredWorkspaceContextTransitionDto>> Handle(
        ListExpiredWorkspaceContextTransitionsQuery query,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.BatchSize);

        IReadOnlyList<WorkspaceTransitionExpiryItem> items =
            await expiryStore.ListExpiredPendingAsync(query.Now, query.BatchSize, ct);

        return items.Select(item => new ExpiredWorkspaceContextTransitionDto(
                item.TransitionId,
                item.UserId,
                item.SourceCorrelationDigest))
            .ToArray();
    }
}
