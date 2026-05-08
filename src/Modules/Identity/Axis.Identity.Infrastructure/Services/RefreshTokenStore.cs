using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class RefreshTokenStore(IdentityDbContext context) : IRefreshTokenStore
{
    public async Task<Guid> CreateAsync(Guid userId, Guid orgId, string tokenHash, string deviceInfo, DateTime expiresAt, CancellationToken ct = default)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationId = orgId,
            TokenHash = tokenHash,
            DeviceInfo = deviceInfo,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync(ct);
        return token.Id;
    }

    public async Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var t = await context.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (t is null || !t.IsActive) return null;

        return Map(t);
    }

    public async Task<IReadOnlyList<RefreshTokenInfo>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.LastUsedAt)
            .ToListAsync(ct);

        return tokens.Select(Map).ToList();
    }

    public async Task RevokeAsync(Guid tokenId, CancellationToken ct = default)
    {
        var token = await context.RefreshTokens.FindAsync([tokenId], ct);
        if (token is null) return;

        token.RevokedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, DateTime.UtcNow), ct);
    }

    public async Task UpdateLastUsedAsync(Guid tokenId, CancellationToken ct = default)
    {
        await context.RefreshTokens
            .Where(r => r.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastUsedAt, DateTime.UtcNow), ct);
    }

    private static RefreshTokenInfo Map(RefreshToken t) =>
        new(t.Id, t.UserId, t.OrganizationId, t.DeviceInfo, t.CreatedAt, t.LastUsedAt, t.ExpiresAt);
}
