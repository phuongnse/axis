using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ResendWorkspaceInvitation;

public sealed record ResendWorkspaceInvitationCommand(
    Guid ActorUserId,
    Guid WorkspaceId,
    Guid InvitationId,
    int ExpectedRevision,
    string CorrelationId,
    string ActorDisplayName) : ICommand<WorkspaceInvitationLifecycleDto>;
