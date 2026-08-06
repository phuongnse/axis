using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;

public sealed record CompleteWorkspaceContextTransitionCommand(
    Guid TransitionId,
    Guid UserId,
    string TargetCorrelationDigest,
    string CorrelationId) : ICommand<WorkspaceContextTransitionDto>;
