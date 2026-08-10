using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ExpireWorkspaceContextTransition;

public sealed record ExpireWorkspaceContextTransitionCommand(
    Guid TransitionId,
    Guid UserId,
    string SourceCorrelationDigest) : ICommand<WorkspaceContextTransitionDto>;
