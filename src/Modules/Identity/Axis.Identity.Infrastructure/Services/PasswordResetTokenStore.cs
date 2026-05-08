using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class PasswordResetTokenStore(IdentityDbContext context) : IPasswordResetTokenStore
{
    public async Task CreateAsync(
        Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default)
    {
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.PasswordResetTokens.AddAsync(token, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<Guid?> FindUserIdByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var token = await context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        return token is { } t && t.IsValid ? t.UserId : null;
    }

    public async Task InvalidateAsync(string tokenHash, CancellationToken ct = default)
    {
        var token = await context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is not null)
        {
            token.UsedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await context.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.UsedAt = DateTime.UtcNow;

        if (tokens.Count > 0)
            await context.SaveChangesAsync(ct);
    }
}
