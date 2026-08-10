using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;

public sealed record CompensateWorkspaceContextTransitionCommand(
    Guid TransitionId,
    Guid UserId,
    string SourceCorrelationDigest,
    string CorrelationId) : ICommand<WorkspaceContextTransitionDto>;
