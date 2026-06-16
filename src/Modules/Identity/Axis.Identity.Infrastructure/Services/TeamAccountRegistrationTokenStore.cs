using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TeamAccountRegistrationTokenStore(IdentityDbContext context)
    : ITeamAccountRegistrationTokenStore
{
    public async Task CreateVerificationAsync(
        Guid teamAccountId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            teamAccountId,
            tokenHash,
            TeamAccountRegistrationTokenPurpose.ContactEmailVerification,
            expiresAt,
            ct);
    }

    public async Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        TeamAccountRegistrationToken? token = await context.Set<TeamAccountRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == TeamAccountRegistrationTokenPurpose.ContactEmailVerification,
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
        return Result.Success(token.TeamAccountId);
    }

    public async Task<Guid?> ResolveTeamAccountIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        TeamAccountRegistrationToken? token = await context.Set<TeamAccountRegistrationToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == TeamAccountRegistrationTokenPurpose.ContactEmailVerification,
                ct);

        if (token is null || DateTime.UtcNow >= token.ExpiresAt)
            return null;

        return token.TeamAccountId;
    }

    public async Task CreateFirstUserSetupAsync(
        Guid teamAccountId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            teamAccountId,
            tokenHash,
            TeamAccountRegistrationTokenPurpose.FirstUserSetup,
            expiresAt,
            ct);
    }

    public async Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        TeamAccountRegistrationToken? token = await context.Set<TeamAccountRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == TeamAccountRegistrationTokenPurpose.FirstUserSetup,
                ct);

        if (token is null)
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid team account setup link.");

        if (token.UsedAt is not null)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This team account setup link has already been used.");

        if (now >= token.ExpiresAt)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This team account setup link has expired. Please request a new setup link.");

        token.UsedAt = now;
        token.UsedByUserId = userId;
        return Result.Success(token.TeamAccountId);
    }

    private async Task CreateAsync(
        Guid teamAccountId,
        string tokenHash,
        TeamAccountRegistrationTokenPurpose purpose,
        DateTime expiresAt,
        CancellationToken ct)
    {
        TeamAccountRegistrationToken token = new()
        {
            Id = Guid.NewGuid(),
            TeamAccountId = teamAccountId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.Set<TeamAccountRegistrationToken>().AddAsync(token, ct);
    }
}
