using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WorkspaceRegistrationTokenStore(IdentityDbContext context)
    : IWorkspaceRegistrationTokenStore
{
    public async Task CreateVerificationAsync(
        Guid workspaceId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            workspaceId,
            tokenHash,
            WorkspaceRegistrationTokenPurpose.ContactEmailVerification,
            expiresAt,
            ct);
    }

    public async Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceRegistrationToken? token = await context.Set<WorkspaceRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == WorkspaceRegistrationTokenPurpose.ContactEmailVerification,
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
        return Result.Success(token.WorkspaceId);
    }

    public async Task<Guid?> ResolveWorkspaceIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        WorkspaceRegistrationToken? token = await context.Set<WorkspaceRegistrationToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == WorkspaceRegistrationTokenPurpose.ContactEmailVerification,
                ct);

        if (token is null || DateTime.UtcNow >= token.ExpiresAt)
            return null;

        return token.WorkspaceId;
    }

    public async Task CreateFirstUserSetupAsync(
        Guid workspaceId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await CreateAsync(
            workspaceId,
            tokenHash,
            WorkspaceRegistrationTokenPurpose.FirstUserSetup,
            expiresAt,
            ct);
    }

    public async Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceRegistrationToken? token = await context.Set<WorkspaceRegistrationToken>()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash
                && t.Purpose == WorkspaceRegistrationTokenPurpose.FirstUserSetup,
                ct);

        if (token is null)
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid Workspace setup link.");

        if (token.UsedAt is not null)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This Workspace setup link has already been used.");

        if (now >= token.ExpiresAt)
            return Result.Failure<Guid>(
                ErrorCodes.BusinessRule,
                "This Workspace setup link has expired. Please request a new setup link.");

        token.UsedAt = now;
        token.UsedByUserId = userId;
        return Result.Success(token.WorkspaceId);
    }

    private async Task CreateAsync(
        Guid workspaceId,
        string tokenHash,
        WorkspaceRegistrationTokenPurpose purpose,
        DateTime expiresAt,
        CancellationToken ct)
    {
        WorkspaceRegistrationToken token = new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        await context.Set<WorkspaceRegistrationToken>().AddAsync(token, ct);
    }
}
