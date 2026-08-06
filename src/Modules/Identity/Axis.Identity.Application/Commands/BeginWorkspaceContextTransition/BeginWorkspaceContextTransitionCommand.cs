using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;

public sealed record BeginWorkspaceContextTransitionCommand(
    Guid UserId,
    Guid SourceWorkspaceId,
    Guid TargetWorkspaceId,
    string SourceCorrelationDigest,
    string TargetCorrelationDigest,
    string CorrelationId) : ICommand<WorkspaceContextTransitionDto>;

public sealed record WorkspaceContextTransitionDto(
    Guid TransitionId,
    Guid SourceWorkspaceId,
    Guid TargetWorkspaceId,
    string Status,
    int Revision,
    DateTime ExpiresAt);
