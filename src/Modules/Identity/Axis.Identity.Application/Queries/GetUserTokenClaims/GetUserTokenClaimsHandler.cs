using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed class GetUserTokenClaimsHandler(
    IUserRepository userRepo,
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

        if (query.OrganizationId.HasValue && query.OrganizationId.Value != user.OrganizationId)
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Invalid organization scope for this user.");
        }

        Guid orgId = user.OrganizationId;

        IReadOnlyList<Role> roles = await roleRepo.GetByIdsAsync(user.RoleIds, orgId, cancellationToken);
        List<string> permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result.Success(new UserTokenClaimsDto(
            user.Id,
            orgId,
            user.Email.Value,
            $"{user.FirstName} {user.LastName}",
            permissions));
    }
}
