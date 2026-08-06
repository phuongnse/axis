using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.FailWorkspaceContextTransition;

public sealed record FailWorkspaceContextTransitionCommand(
    Guid TransitionId,
    Guid UserId,
    string SourceCorrelationDigest,
    string CorrelationId) : ICommand<WorkspaceContextTransitionDto>;
