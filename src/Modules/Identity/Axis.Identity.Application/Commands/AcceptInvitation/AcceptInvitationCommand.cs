using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.AcceptInvitation;

/// <summary>US-018: Accept an invitation and create a user account.</summary>
public sealed record AcceptInvitationCommand(
    string Token,
    string FirstName,
    string LastName,
    string Password) : ICommand<AcceptInvitationResult>;

public sealed record AcceptInvitationResult(
    Guid UserId,
    Guid OrganizationId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions);
