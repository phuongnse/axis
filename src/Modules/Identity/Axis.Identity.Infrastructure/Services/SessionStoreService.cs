using Axis.Identity.Application.Services;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class SessionStoreService(IOpenIddictTokenManager tokenManager) : ISessionStore
{
    public async Task<IReadOnlyList<UserSession>> GetByUserAsync(
        Guid userId, string currentTokenId, CancellationToken ct = default)
    {
        List<UserSession> sessions = [];

        await foreach (object token in tokenManager.FindBySubjectAsync(userId.ToString(), ct))
        {
            string? type = await tokenManager.GetTypeAsync(token, ct);
            string? status = await tokenManager.GetStatusAsync(token, ct);

            if (type != TokenTypeHints.RefreshToken || status != Statuses.Valid)
                continue;

            string? id = await tokenManager.GetIdAsync(token, ct);
            DateTimeOffset? expiry = await tokenManager.GetExpirationDateAsync(token, ct);
            DateTimeOffset? created = await tokenManager.GetCreationDateAsync(token, ct);

            sessions.Add(new UserSession(
                SessionId: id ?? string.Empty,
                UserId: userId,
                DeviceInfo: "Unknown",
                LastActivity: created?.UtcDateTime ?? DateTime.UtcNow,
                ExpiresAt: expiry?.UtcDateTime ?? DateTime.UtcNow,
                IsCurrentSession: id == currentTokenId));
        }

        return sessions;
    }

    public async Task RevokeAsync(string sessionId, Guid userId, CancellationToken ct = default)
    {
        object? token = await tokenManager.FindByIdAsync(sessionId, ct);
        if (token is not null)
            await tokenManager.TryRevokeAsync(token, ct);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        // Enumerate all tokens for the user and revoke each one individually.
        // OpenIddict 5.x does not expose a single-call bulk revocation by subject.
        await foreach (object token in tokenManager.FindBySubjectAsync(userId.ToString(), ct))
            await tokenManager.TryRevokeAsync(token, ct);
    }
}
