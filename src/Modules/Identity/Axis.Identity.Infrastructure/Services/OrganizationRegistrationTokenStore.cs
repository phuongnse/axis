using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class OrganizationRegistrationTokenStore(IdentityDbContext context)
    : IOrganizationRegistrationTokenStore
{
    public async Task CreateVerificationAsync(
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            organizationId,
            tokenHash,
            OrganizationRegistrationTokenPurpose.ContactEmailVerification,
            expiresAt,
            ct);
    }

    public async Task<OrganizationVerificationTokenResolveResult> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        int consumed = await context.Set<OrganizationRegistrationToken>()
            .Where(t =>
                t.TokenHash == tokenHash
                && t.Purpose == OrganizationRegistrationTokenPurpose.ContactEmailVerification
                && t.UsedAt == null
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        if (consumed == 1)
        {
            Guid organizationId = await context.Set<OrganizationRegistrationToken>()
                .Where(t => t.TokenHash == tokenHash)
                .Select(t => t.OrganizationId)
                .FirstAsync(ct);
            return new OrganizationVerificationTokenResolveResult(
                OrganizationVerificationTokenState.Valid,
                organizationId);
        }

        OrganizationRegistrationToken? token = await GetTokenAsync(
            tokenHash,
            OrganizationRegistrationTokenPurpose.ContactEmailVerification,
            ct);

        return token is null
            ? new OrganizationVerificationTokenResolveResult(OrganizationVerificationTokenState.NotFound, null)
            : token.UsedAt is not null
                ? new OrganizationVerificationTokenResolveResult(
                    OrganizationVerificationTokenState.AlreadyUsed,
                    token.OrganizationId)
                : now >= token.ExpiresAt
                    ? new OrganizationVerificationTokenResolveResult(
                        OrganizationVerificationTokenState.Expired,
                        token.OrganizationId)
                    : new OrganizationVerificationTokenResolveResult(
                        OrganizationVerificationTokenState.AlreadyUsed,
                        token.OrganizationId);
    }

    public async Task<Guid?> ResolveOrganizationIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        OrganizationRegistrationToken? token = await context.Set<OrganizationRegistrationToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == OrganizationRegistrationTokenPurpose.ContactEmailVerification,
                ct);

        if (token is null || DateTime.UtcNow >= token.ExpiresAt)
            return null;

        return token.OrganizationId;
    }

    public async Task CreateFirstUserSetupAsync(
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            organizationId,
            tokenHash,
            OrganizationRegistrationTokenPurpose.FirstUserSetup,
            expiresAt,
            ct);
    }

    public async Task<OrganizationSetupTokenConsumeResult> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        int consumed = await context.Set<OrganizationRegistrationToken>()
            .Where(t =>
                t.TokenHash == tokenHash
                && t.Purpose == OrganizationRegistrationTokenPurpose.FirstUserSetup
                && t.UsedAt == null
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.UsedAt, now)
                .SetProperty(t => t.UsedByUserId, (Guid?)userId),
                ct);

        if (consumed == 1)
        {
            Guid organizationId = await context.Set<OrganizationRegistrationToken>()
                .Where(t => t.TokenHash == tokenHash)
                .Select(t => t.OrganizationId)
                .FirstAsync(ct);
            return new OrganizationSetupTokenConsumeResult(
                OrganizationSetupTokenState.Valid,
                organizationId);
        }

        OrganizationRegistrationToken? token = await GetTokenAsync(
            tokenHash,
            OrganizationRegistrationTokenPurpose.FirstUserSetup,
            ct);

        return token is null
            ? new OrganizationSetupTokenConsumeResult(OrganizationSetupTokenState.NotFound, null)
            : token.UsedAt is not null
                ? new OrganizationSetupTokenConsumeResult(
                    OrganizationSetupTokenState.AlreadyUsed,
                    token.OrganizationId)
                : now >= token.ExpiresAt
                    ? new OrganizationSetupTokenConsumeResult(
                        OrganizationSetupTokenState.Expired,
                        token.OrganizationId)
                    : new OrganizationSetupTokenConsumeResult(
                        OrganizationSetupTokenState.AlreadyUsed,
                        token.OrganizationId);
    }

    private async Task CreateAsync(
        Guid organizationId,
        string tokenHash,
        OrganizationRegistrationTokenPurpose purpose,
        DateTime expiresAt,
        CancellationToken ct)
    {
        OrganizationRegistrationToken token = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.Set<OrganizationRegistrationToken>().AddAsync(token, ct);
        await context.SaveChangesAsync(ct);
    }

    private Task<OrganizationRegistrationToken?> GetTokenAsync(
        string tokenHash,
        OrganizationRegistrationTokenPurpose purpose,
        CancellationToken ct) =>
        context.Set<OrganizationRegistrationToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Purpose == purpose, ct);
}
