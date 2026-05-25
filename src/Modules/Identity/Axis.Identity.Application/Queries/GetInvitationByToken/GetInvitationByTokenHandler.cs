using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetInvitationByToken;

public sealed class GetInvitationByTokenHandler(IInvitationRepository invitationRepo)
    : IQueryHandler<GetInvitationByTokenQuery, InvitationByTokenDto?>
{
    public async Task<InvitationByTokenDto?> Handle(
        GetInvitationByTokenQuery query,
        CancellationToken cancellationToken)
    {
        Invitation? invitation = await invitationRepo.GetByTokenAsync(query.Token, cancellationToken);
        if (invitation is null)
            return null;

        return new InvitationByTokenDto(
            invitation.Id,
            invitation.Email.Value,
            invitation.Status.ToString().ToLowerInvariant(),
            invitation.ExpiresAt);
    }
}
