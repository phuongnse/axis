using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserPermissions;

public sealed class GetUserPermissionsHandler(
    IUserRepository userRepo,
    IWorkspaceMembershipRepository membershipRepo,
    IRoleRepository roleRepo)
    : IQueryHandler<GetUserPermissionsQuery, Result<GetUserPermissionsResult>>
{
    public async Task<Result<GetUserPermissionsResult>> Handle(
        GetUserPermissionsQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdAsync(query.UserId, query.workspaceId, cancellationToken);
        if (user is null)
            return Result.Failure<GetUserPermissionsResult>(ErrorCodes.NotFound, "User not found.");

        if (user.Status == UserStatus.Inactive)
            return Result.Success(new GetUserPermissionsResult([]));

        WorkspaceMembership? membership =
            await membershipRepo.GetByUserAndWorkspaceAsync(
                query.UserId,
                query.workspaceId,
                cancellationToken);
        if (membership is null || membership.Status == WorkspaceMembershipStatus.Inactive)
            return Result.Success(new GetUserPermissionsResult([]));

        IReadOnlyList<Role> roles =
            await roleRepo.GetByIdsAsync(membership.RoleIds, membership.workspaceId, cancellationToken);

        List<string> permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result.Success(new GetUserPermissionsResult(permissions));
    }
}
