using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed class GetUserTokenClaimsHandler(
    IUserRepository userRepo,
    ITeamAccountMembershipRepository membershipRepo,
    IRoleRepository roleRepo)
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

        TeamAccountMembership? membership = query.TeamAccountId is Guid teamAccountId
            ? await membershipRepo.GetByUserAndTeamAccountAsync(user.Id, teamAccountId, cancellationToken)
            : await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);

        if (query.TeamAccountId.HasValue && membership is null)
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Invalid team account scope for this user.");
        }

        if (membership is null)
        {
            return Result.Success(new UserTokenClaimsDto(
                user.Id,
                null,
                user.Email.Value,
                $"{user.FirstName} {user.LastName}",
                []));
        }

        IReadOnlyList<Role> roles = await roleRepo.GetByIdsAsync(
            membership.RoleIds,
            membership.TeamAccountId,
            cancellationToken);
        List<string> permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result.Success(new UserTokenClaimsDto(
            user.Id,
            membership.TeamAccountId,
            user.Email.Value,
            $"{user.FirstName} {user.LastName}",
            permissions));
    }
}
