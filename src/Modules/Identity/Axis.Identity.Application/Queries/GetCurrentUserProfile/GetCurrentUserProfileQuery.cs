using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed record GetCurrentUserProfileQuery(Guid UserId, Guid OrganizationId, IReadOnlyList<string> Permissions)
    : IQuery<CurrentUserProfileDto?>;

public sealed record CurrentUserProfileDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? AvatarUrl,
    bool IsActive,
    Guid OrgId,
    IReadOnlyList<string> Permissions);
