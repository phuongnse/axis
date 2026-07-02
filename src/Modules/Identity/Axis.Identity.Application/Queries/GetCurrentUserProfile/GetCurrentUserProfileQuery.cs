using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed record GetCurrentUserProfileQuery(Guid UserId, Guid? workspaceId)
    : IQuery<CurrentUserProfileDto?>;

public sealed record CurrentUserProfileDto(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    Guid? WorkspaceId,
    IReadOnlyList<UserWorkspaceDto> Workspaces);

public sealed record UserWorkspaceDto(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    bool IsCurrent);
