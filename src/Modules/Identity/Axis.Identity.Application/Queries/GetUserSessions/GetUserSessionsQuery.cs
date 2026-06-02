using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetUserSessions;

public record GetUserSessionsQuery(Guid UserId, string CurrentTokenId) : IQuery<IReadOnlyList<UserSession>>;

/// <summary>
/// API response shape for a user session. Deliberately excludes <c>UserId</c>
/// (internal) and exposes <c>IsCurrent</c> as <c>is_current</c> on the wire.
/// </summary>
public sealed record UserSessionResponse(
    string SessionId,
    string DeviceInfo,
    DateTime LastActivity,
    DateTime ExpiresAt,
    bool IsCurrent);
