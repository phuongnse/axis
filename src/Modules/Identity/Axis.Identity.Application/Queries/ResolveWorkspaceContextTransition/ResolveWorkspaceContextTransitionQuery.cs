using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ResolveWorkspaceContextTransition;

public enum WorkspaceContextTransitionCorrelationRole
{
    Source,
    Target,
}

public sealed record ResolveWorkspaceContextTransitionQuery(
    Guid UserId,
    string CorrelationDigest,
    WorkspaceContextTransitionCorrelationRole Role)
    : IQuery<Result<WorkspaceContextTransitionDto>>;
