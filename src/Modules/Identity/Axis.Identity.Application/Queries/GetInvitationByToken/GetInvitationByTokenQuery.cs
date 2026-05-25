using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetInvitationByToken;

public sealed record GetInvitationByTokenQuery(string Token)
    : IQuery<InvitationByTokenDto?>;

public sealed record InvitationByTokenDto(
    Guid InvitationId,
    string Email,
    string Status,
    DateTime ExpiresAt);
