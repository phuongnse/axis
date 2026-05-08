using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetUserSessions;

public sealed class GetUserSessionsHandler(ISessionStore sessionStore)
    : IQueryHandler<GetUserSessionsQuery, IReadOnlyList<UserSession>>
{
    public async Task<IReadOnlyList<UserSession>> Handle(
        GetUserSessionsQuery query, CancellationToken cancellationToken) =>
        await sessionStore.GetByUserAsync(query.UserId, query.CurrentTokenId, cancellationToken);
}
