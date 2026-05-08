using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetUserSessions;

public record GetUserSessionsQuery(Guid UserId, string CurrentTokenId) : IQuery<IReadOnlyList<UserSession>>;
