using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed class GetUserTokenClaimsHandler(
    IUserRepository userRepo,
    ITenantMembershipRepository membershipRepo,
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

        TenantMembership? membership = query.tenantId is Guid tenantId
            ? await membershipRepo.GetByUserAndTenantAsync(user.Id, tenantId, cancellationToken)
            : await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);

        if (query.tenantId.HasValue && membership is null)
        {
            return Result.Failure<UserTokenClaimsDto>(
                ErrorCodes.BusinessRule,
                "Invalid Tenant scope for this user.");
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
            membership.tenantId,
            cancellationToken);
        List<string> permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result.Success(new UserTokenClaimsDto(
            user.Id,
            membership.tenantId,
            user.Email.Value,
            $"{user.FirstName} {user.LastName}",
            permissions));
    }
}
