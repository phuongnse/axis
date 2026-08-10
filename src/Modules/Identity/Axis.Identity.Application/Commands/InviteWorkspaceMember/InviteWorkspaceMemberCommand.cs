using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.InviteWorkspaceMember;

public sealed record InviteWorkspaceMemberCommand(
    Guid InviterUserId,
    Guid WorkspaceId,
    string Email,
    string RequestedRole,
    string CorrelationId) : ICommand<InviteWorkspaceMemberDto>;

public sealed record InviteWorkspaceMemberDto(
    string Outcome,
    string RequestedRole,
    WorkspaceInvitationLifecycleDto? Invitation);
