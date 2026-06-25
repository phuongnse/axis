using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class EmailVerificationTokenStore(IdentityDbContext context) : IEmailVerificationTokenStore
{
    public async Task CreateAsync(
        Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default)
    {
        EmailVerificationToken token = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.Set<EmailVerificationToken>().AddAsync(token, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<EmailVerificationTokenResolveResult> ResolveForVerificationAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        int consumed = await context.Set<EmailVerificationToken>()
            .Where(t => t.TokenHash == tokenHash && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        if (consumed == 1)
        {
            Guid userId = await context.Set<EmailVerificationToken>()
                .Where(t => t.TokenHash == tokenHash)
                .Select(t => t.UserId)
                .FirstAsync(ct);
            return new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, userId);
        }

        EmailVerificationToken? token = await context.Set<EmailVerificationToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is null)
            return new EmailVerificationTokenResolveResult(EmailVerificationTokenState.NotFound, null);

        if (token.UsedAt is not null)
            return new EmailVerificationTokenResolveResult(EmailVerificationTokenState.AlreadyUsed, token.UserId);

        if (now >= token.ExpiresAt)
            return new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Expired, token.UserId);

        return new EmailVerificationTokenResolveResult(EmailVerificationTokenState.AlreadyUsed, token.UserId);
    }

    public async Task InvalidateAsync(string tokenHash, CancellationToken ct = default)
    {
        EmailVerificationToken? token = await context.Set<EmailVerificationToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is not null)
        {
            token.UsedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        List<EmailVerificationToken> tokens = await context.Set<EmailVerificationToken>()
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(ct);

        foreach (EmailVerificationToken token in tokens)
            token.UsedAt = DateTime.UtcNow;

        if (tokens.Count > 0)
            await context.SaveChangesAsync(ct);
    }
}
