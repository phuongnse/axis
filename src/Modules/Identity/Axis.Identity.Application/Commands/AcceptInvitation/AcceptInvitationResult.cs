namespace Axis.Identity.Application.Commands.AcceptInvitation;

public sealed record AcceptInvitationResult(
    Guid UserId,
    Guid workspaceId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions);
