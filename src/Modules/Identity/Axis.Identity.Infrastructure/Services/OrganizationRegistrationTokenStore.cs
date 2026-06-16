using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Shared.Domain.Primitives;
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

    public async Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        OrganizationRegistrationToken? token = await context.Set<OrganizationRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == OrganizationRegistrationTokenPurpose.ContactEmailVerification,
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
        return Result.Success(token.OrganizationId);
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

    public async Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        OrganizationRegistrationToken? token = await context.Set<OrganizationRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == OrganizationRegistrationTokenPurpose.FirstUserSetup,
                ct);

        if (token is null)
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid organization setup link.");

        if (token.UsedAt is not null)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This organization setup link has already been used.");

        if (now >= token.ExpiresAt)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This organization setup link has expired. Please request a new setup link.");

        token.UsedAt = now;
        token.UsedByUserId = userId;
        return Result.Success(token.OrganizationId);
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
    }
}
