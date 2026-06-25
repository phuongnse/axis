using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed class GetUserTokenClaimsHandler(
    IUserRepository userRepo,
    IWorkspaceMembershipRepository membershipRepo,
    IWorkspaceRepository workspaceRepo)
    : IQueryHandler<GetUserTokenClaimsQuery, Result<UserTokenClaimsDto>>
{
    public async Task<Result<UserTokenClaimsDto>> Handle(
        GetUserTokenClaimsQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdPlatformWideAsync(query.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.NotFound,
                "The account is no longer active.");
        }

        WorkspaceMembership? membership = query.workspaceId is Guid workspaceId
            ? await membershipRepo.GetByUserAndWorkspaceAsync(user.Id, workspaceId, cancellationToken)
            : await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);

        if (query.workspaceId.HasValue && membership is null)
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Invalid Workspace scope for this user.");
        }

        if (membership is null)
        {
            return Result.Success(new UserTokenClaimsDto(
                user.Id,
                null,
                user.Email.Value,
                $"{user.FirstName} {user.LastName}"));
        }

        Workspace? workspace = await workspaceRepo.GetByIdAsync(membership.workspaceId, cancellationToken);
        if (workspace is null || !workspace.AllowsSignIn())
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Workspace is not available for sign-in.");
        }

        return Result.Success(new UserTokenClaimsDto(
            user.Id,
            membership.workspaceId,
            user.Email.Value,
            $"{user.FirstName} {user.LastName}"));
    }
}
