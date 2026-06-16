using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TeamAccountIdentityPurger(
    IdentityDbContext context,
    ITeamAccountLogoStorageService logoStorage) : ITeamAccountIdentityPurger
{
    public async Task PurgeAsync(Guid teamAccountId, string? logoUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(logoUrl))
        {
            try
            {
                await logoStorage.DeleteLogoAsync(logoUrl, cancellationToken);
            }
            catch (Exception)
            {
                // Best-effort file cleanup during hard delete.
            }
        }

        IQueryable<Guid> userIds = context.TeamAccountMemberships
            .Where(m => m.TeamAccountId == teamAccountId)
            .Select(m => m.UserId);

        await context.EmailVerificationTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.TeamAccountRegistrationTokens
            .Where(t => t.TeamAccountId == teamAccountId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.PasswordResetTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Invitations
            .Where(i => i.TeamAccountId == teamAccountId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.TenantModuleProvisions
            .Where(p => p.TeamAccountId == teamAccountId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Roles
            .Where(r => r.TeamAccountId == teamAccountId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.TeamAccountMemberships
            .Where(m => m.TeamAccountId == teamAccountId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.TeamAccounts
            .Where(o => o.Id == teamAccountId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
