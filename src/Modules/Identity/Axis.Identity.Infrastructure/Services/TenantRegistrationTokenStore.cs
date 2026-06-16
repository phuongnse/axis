using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TenantRegistrationTokenStore(IdentityDbContext context)
    : ITenantRegistrationTokenStore
{
    public async Task CreateVerificationAsync(
        Guid tenantId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            tenantId,
            tokenHash,
            TenantRegistrationTokenPurpose.ContactEmailVerification,
            expiresAt,
            ct);
    }

    public async Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        TenantRegistrationToken? token = await context.Set<TenantRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == TenantRegistrationTokenPurpose.ContactEmailVerification,
                ct);

        if (token is null)
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (token.UsedAt is not null)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");

        if (now >= token.ExpiresAt)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This verification link has expired. Please request a new verification email.");

        token.UsedAt = now;
        return Result.Success(token.tenantId);
    }

    public async Task<Guid?> ResolvetenantIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        TenantRegistrationToken? token = await context.Set<TenantRegistrationToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == TenantRegistrationTokenPurpose.ContactEmailVerification,
                ct);

        if (token is null || DateTime.UtcNow >= token.ExpiresAt)
            return null;

        return token.tenantId;
    }

    public async Task CreateFirstUserSetupAsync(
        Guid tenantId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            tenantId,
            tokenHash,
            TenantRegistrationTokenPurpose.FirstUserSetup,
            expiresAt,
            ct);
    }

    public async Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        TenantRegistrationToken? token = await context.Set<TenantRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == TenantRegistrationTokenPurpose.FirstUserSetup,
                ct);

        if (token is null)
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid Tenant setup link.");

        if (token.UsedAt is not null)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This Tenant setup link has already been used.");

        if (now >= token.ExpiresAt)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This Tenant setup link has expired. Please request a new setup link.");

        token.UsedAt = now;
        token.UsedByUserId = userId;
        return Result.Success(token.tenantId);
    }

    private async Task CreateAsync(
        Guid tenantId,
        string tokenHash,
        TenantRegistrationTokenPurpose purpose,
        DateTime expiresAt,
        CancellationToken ct)
    {
        TenantRegistrationToken token = new()
        {
            Id = Guid.NewGuid(),
            tenantId = tenantId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.Set<TenantRegistrationToken>().AddAsync(token, ct);
    }
}
