using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetExternalRegistrationSession;

public sealed class GetExternalRegistrationSessionHandler(
    IExternalRegistrationSessionRepository sessionRepo)
    : IQueryHandler<GetExternalRegistrationSessionQuery, ExternalRegistrationSessionDto?>
{
    public async Task<ExternalRegistrationSessionDto?> Handle(
        GetExternalRegistrationSessionQuery query,
        CancellationToken cancellationToken)
    {
        ExternalRegistrationSession? session =
            await sessionRepo.GetByIdAsync(query.SessionId, cancellationToken);

        if (session is null || session.IsExpired || session.IsCompleted)
            return null;

        return new ExternalRegistrationSessionDto(
            session.Email.Value,
            session.DisplayName);
    }
}
