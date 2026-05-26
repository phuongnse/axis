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
        PasswordResetToken token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.PasswordResetTokens.AddAsync(token, ct);
    }

    public async Task<Guid?> FindUserIdByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        PasswordResetToken? token = await context.PasswordResetTokens
                    .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        return token is { } t && t.IsValid ? t.UserId : null;
    }

    public async Task InvalidateAsync(string tokenHash, CancellationToken ct = default)
    {
        PasswordResetToken? token = await context.PasswordResetTokens
                    .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is not null)
            token.UsedAt = DateTime.UtcNow;
    }

    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        List<PasswordResetToken> tokens = await context.PasswordResetTokens
                    .Where(t => t.UserId == userId && t.UsedAt == null)
                    .ToListAsync(ct);

        foreach (PasswordResetToken token in tokens)
            token.UsedAt = DateTime.UtcNow;
    }
}
