using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class OrganizationIdentityPurger(
    IdentityDbContext context,
    IOrganizationLogoStorageService logoStorage) : IOrganizationIdentityPurger
{
    public async Task PurgeAsync(Guid organizationId, string? logoUrl, CancellationToken cancellationToken)
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

        IQueryable<Guid> userIds = context.Users
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => u.Id);

        await context.EmailVerificationTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.PasswordResetTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Invitations
            .Where(i => i.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.TenantModuleProvisions
            .Where(p => p.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Roles
            .Where(r => r.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Users
            .Where(u => u.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Organizations
            .Where(o => o.Id == organizationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
