using Axis.Identity.Application.Services;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class SessionStoreService(IRefreshTokenStore tokenStore) : ISessionStore
{
    public async Task<IReadOnlyList<UserSession>> GetByUserAsync(Guid userId, string currentTokenId, CancellationToken ct = default)
    {
        var tokens = await tokenStore.GetActiveByUserAsync(userId, ct);

        return tokens.Select(t => new UserSession(
            SessionId: t.Id.ToString(),
            UserId: t.UserId,
            DeviceInfo: t.DeviceInfo,
            LastActivity: t.LastUsedAt,
            ExpiresAt: t.ExpiresAt,
            IsCurrentSession: t.Id.ToString() == currentTokenId))
            .ToList();
    }

    public async Task RevokeAsync(string sessionId, Guid userId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(sessionId, out var tokenId))
            throw new InvalidOperationException($"Invalid session ID: {sessionId}");

        await tokenStore.RevokeAsync(tokenId, ct);
    }

    public Task RevokeAllAsync(Guid userId, CancellationToken ct = default) =>
        tokenStore.RevokeAllForUserAsync(userId, ct);
}
