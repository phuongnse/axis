namespace Axis.Identity.Application.Services;

public record UserSession(
    string SessionId,
    Guid UserId,
    string DeviceInfo,
    DateTime LastActivity,
    DateTime ExpiresAt,
    bool IsCurrentSession);

public interface ISessionStore
{
    Task<IReadOnlyList<UserSession>> GetByUserAsync(Guid userId, string currentTokenId, CancellationToken ct = default);
    Task RevokeAsync(string sessionId, Guid userId, CancellationToken ct = default);
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);
}
