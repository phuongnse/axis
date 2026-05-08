namespace Axis.Identity.Application.Services;

public record RefreshTokenInfo(
    Guid Id,
    Guid UserId,
    Guid OrganizationId,
    string DeviceInfo,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt);

public interface IRefreshTokenStore
{
    Task<Guid> CreateAsync(Guid userId, Guid orgId, string tokenHash, string deviceInfo, DateTime expiresAt, CancellationToken ct = default);
    Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshTokenInfo>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task RevokeAsync(Guid tokenId, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
    Task UpdateLastUsedAsync(Guid tokenId, CancellationToken ct = default);
}
