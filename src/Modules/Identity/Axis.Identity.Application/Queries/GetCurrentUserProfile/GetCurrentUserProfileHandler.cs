using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed class GetCurrentUserProfileHandler(
    IUserRepository userRepository,
    IWorkspaceRepository workspaceRepository,
    IWorkspaceMembershipRepository workspaceMemberships)
    : IQueryHandler<GetCurrentUserProfileQuery, CurrentUserProfileDto?>
{
    public async Task<CurrentUserProfileDto?> Handle(
        GetCurrentUserProfileQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await userRepository.GetByIdPlatformWideAsync(query.UserId, cancellationToken);
        if (user is null)
            return null;

        List<UserWorkspaceDto> workspaces = [];
        foreach (WorkspaceMembership membership in await workspaceMemberships.ListActiveForUserAsync(user.Id, cancellationToken))
        {
            Workspace? personalWorkspace = await workspaceRepository.GetByIdAsync(membership.WorkspaceId, cancellationToken);
            if (personalWorkspace is null || !personalWorkspace.AllowsSignIn())
                continue;
            workspaces.Add(new UserWorkspaceDto(
                personalWorkspace.Id,
                personalWorkspace.Name,
                personalWorkspace.Slug.Value,
                personalWorkspace.Type.ToString(),
                query.workspaceId == personalWorkspace.Id));
        }

        return new CurrentUserProfileDto(
            user.Id,
            user.Email.Value,
            user.FullName,
            user.Status == UserStatus.Active,
            user.LanguagePreference?.Value,
            user.ThemePreference?.Value,
            query.workspaceId,
            workspaces
                .OrderBy(workspace => workspace.Type == WorkspaceType.Personal.ToString() ? 0 : 1)
                .ThenBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }
}
