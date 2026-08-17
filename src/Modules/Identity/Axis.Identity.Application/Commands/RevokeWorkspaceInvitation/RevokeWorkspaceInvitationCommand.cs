using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RevokeWorkspaceInvitation;

public sealed record RevokeWorkspaceInvitationCommand(
    Guid ActorUserId,
    Guid WorkspaceId,
    Guid InvitationId,
    int ExpectedRevision,
    string CorrelationId,
    string ActorDisplayName) : ICommand<WorkspaceInvitationLifecycleDto>;
