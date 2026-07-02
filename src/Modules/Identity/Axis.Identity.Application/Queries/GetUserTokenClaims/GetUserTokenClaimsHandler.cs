using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed class GetUserTokenClaimsHandler(
    IUserRepository userRepo,
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

        Workspace? workspace = query.workspaceId is Guid workspaceId
            ? await workspaceRepo.GetByIdAsync(workspaceId, cancellationToken)
            : await workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, cancellationToken);

        if (query.workspaceId.HasValue && (workspace is null || workspace.OwnerUserId != user.Id))
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Invalid workspace scope for this user.");
        }

        if (workspace is null)
        {
            return Result.Success(new UserTokenClaimsDto(
                user.Id,
                null,
                user.Email.Value,
                user.FullName));
        }

        if (!workspace.AllowsSignIn())
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Workspace is not available for sign-in.");
        }

        return Result.Success(new UserTokenClaimsDto(
            user.Id,
            workspace.Id,
            user.Email.Value,
            user.FullName));
    }
}
