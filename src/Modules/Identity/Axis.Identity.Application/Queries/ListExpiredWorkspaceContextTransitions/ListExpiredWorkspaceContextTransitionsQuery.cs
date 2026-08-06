using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListExpiredWorkspaceContextTransitions;

public sealed record ListExpiredWorkspaceContextTransitionsQuery(
    DateTimeOffset Now,
    int BatchSize) : IQuery<IReadOnlyList<ExpiredWorkspaceContextTransitionDto>>;

public sealed record ExpiredWorkspaceContextTransitionDto(
    Guid TransitionId,
    Guid UserId,
    string SourceCorrelationDigest);
