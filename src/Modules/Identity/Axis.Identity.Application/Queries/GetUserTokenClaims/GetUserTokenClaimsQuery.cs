using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserTokenClaims;

public sealed record GetUserTokenClaimsQuery(Guid UserId, Guid? workspaceId)
    : IQuery<Result<UserTokenClaimsDto>>;

public sealed record UserTokenClaimsDto(
    Guid UserId,
    Guid? workspaceId,
    string Email,
    string FullName);
