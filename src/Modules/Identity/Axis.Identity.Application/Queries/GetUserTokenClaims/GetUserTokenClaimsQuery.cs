using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed record GetUserTokenClaimsQuery(Guid UserId, Guid? OrganizationId)
    : IQuery<Result<UserTokenClaimsDto>>;

public sealed record UserTokenClaimsDto(
    Guid UserId,
    Guid? OrganizationId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions);
