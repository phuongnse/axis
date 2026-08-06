using Axis.Identity.Application.Commands;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ResolveWorkspaceContextTransition;

public sealed class ResolveWorkspaceContextTransitionHandler(
    IWorkspaceContextTransitionRepository transitions)
    : IQueryHandler<ResolveWorkspaceContextTransitionQuery, Result<WorkspaceContextTransitionDto>>
{
    public async Task<Result<WorkspaceContextTransitionDto>> Handle(
        ResolveWorkspaceContextTransitionQuery query,
        CancellationToken ct)
    {
        WorkspaceContextTransition? transition = query.Role switch
        {
            WorkspaceContextTransitionCorrelationRole.Source =>
                await transitions.GetBySourceCorrelationDigestAsync(
                    query.UserId,
                    query.CorrelationDigest,
                    ct),
            WorkspaceContextTransitionCorrelationRole.Target =>
                await transitions.GetByTargetCorrelationDigestAsync(
                    query.UserId,
                    query.CorrelationDigest,
                    ct),
            _ => null,
        };

        return transition is null
            ? Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.NotFound,
                "Transition is unavailable.")
            : Result.Success(WorkspaceContextTransitionAudit.ToDto(transition));
    }
}
