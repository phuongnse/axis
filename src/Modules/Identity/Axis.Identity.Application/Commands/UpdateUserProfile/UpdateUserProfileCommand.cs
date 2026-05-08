using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateUserProfile;

/// <summary>US-020: Update a user's full name and optional avatar image.</summary>
public sealed record UpdateUserProfileCommand(
    Guid UserId,
    Guid OrganizationId,
    string FirstName,
    string LastName,
    byte[]? AvatarBytes,
    string? AvatarContentType) : ICommand;
