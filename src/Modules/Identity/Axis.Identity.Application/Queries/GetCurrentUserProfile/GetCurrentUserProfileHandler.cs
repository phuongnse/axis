using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed class GetCurrentUserProfileHandler(
    IUserRepository userRepository,
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

        List<UserWorkspaceDto> workspaces = [];
        Workspace? personalWorkspace =
            await workspaceRepository.GetPersonalByOwnerUserIdAsync(user.Id, cancellationToken);
        if (personalWorkspace is not null && personalWorkspace.AllowsSignIn())
        {
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
            query.workspaceId,
            workspaces
                .OrderBy(workspace => workspace.Type == WorkspaceType.Personal.ToString() ? 0 : 1)
                .ThenBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }
}
