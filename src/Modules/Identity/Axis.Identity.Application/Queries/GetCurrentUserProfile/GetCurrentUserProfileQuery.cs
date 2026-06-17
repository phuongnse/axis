using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed record GetCurrentUserProfileQuery(Guid UserId, Guid? workspaceId, IReadOnlyList<string> Permissions)
    : IQuery<CurrentUserProfileDto?>;

public sealed record CurrentUserProfileDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? AvatarUrl,
    bool IsActive,
    Guid? WorkspaceId,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<UserWorkspaceDto> Workspaces);

public sealed record UserWorkspaceDto(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    bool IsCurrent);
