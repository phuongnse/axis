namespace Axis.Identity.Application.Commands.AcceptInvitation;

public sealed record AcceptInvitationResult(
    Guid UserId,
    Guid TeamAccountId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions);
