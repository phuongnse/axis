using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class tenantIdentityPurger(
    IdentityDbContext context,
    ITenantLogoStorageService logoStorage) : ItenantIdentityPurger
{
    public async Task PurgeAsync(Guid tenantId, string? logoUrl, CancellationToken cancellationToken)
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

        IQueryable<Guid> userIds = context.TenantMemberships
            .Where(m => m.tenantId == tenantId)
            .Select(m => m.UserId);

        await context.EmailVerificationTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.TenantRegistrationTokens
            .Where(t => t.tenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.PasswordResetTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Invitations
            .Where(i => i.tenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.TenantModuleProvisions
            .Where(p => p.tenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Roles
            .Where(r => r.tenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.TenantMemberships
            .Where(m => m.tenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Tenants
            .Where(o => o.Id == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
