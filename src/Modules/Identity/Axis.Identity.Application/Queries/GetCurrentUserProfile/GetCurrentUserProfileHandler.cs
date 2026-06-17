using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed class GetCurrentUserProfileHandler(
    IUserRepository userRepository,
    IWorkspaceMembershipRepository membershipRepository,
    IWorkspaceRepository workspaceRepository)
    : IQueryHandler<GetCurrentUserProfileQuery, CurrentUserProfileDto?>
{
    public async Task<CurrentUserProfileDto?> Handle(
        GetCurrentUserProfileQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await userRepository.GetByIdPlatformWideAsync(query.UserId, cancellationToken);
        if (user is null)
            return null;

        IReadOnlyList<WorkspaceMembership> memberships =
            await membershipRepository.GetByUserIdAsync(user.Id, cancellationToken);
        List<UserWorkspaceDto> workspaces = [];
        foreach (WorkspaceMembership membership in memberships.Where(m => m.Status == WorkspaceMembershipStatus.Active))
        {
            Workspace? workspace = await workspaceRepository.GetByIdAsync(membership.workspaceId, cancellationToken);
            if (workspace is null || !workspace.AllowsSignIn())
                continue;

            workspaces.Add(new UserWorkspaceDto(
                workspace.Id,
                workspace.Name,
                workspace.Slug.Value,
                workspace.Type.ToString(),
                query.workspaceId == workspace.Id));
        }

        return new CurrentUserProfileDto(
            user.Id,
            user.Email.Value,
            user.FirstName,
            user.LastName,
            $"{user.FirstName} {user.LastName}",
            user.AvatarUrl,
            user.Status == UserStatus.Active,
            query.workspaceId,
            query.Permissions,
            workspaces
                .OrderBy(workspace => workspace.Type == WorkspaceType.Personal.ToString() ? 0 : 1)
                .ThenBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }
}
