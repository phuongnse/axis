using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WorkspaceIdentityPurger(
    IdentityDbContext context,
    IWorkspaceLogoStorageService logoStorage) : IWorkspaceIdentityPurger
{
    public async Task PurgeAsync(Guid workspaceId, string? logoUrl, CancellationToken cancellationToken)
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

        IQueryable<Guid> userIds = context.WorkspaceMemberships
            .Where(m => m.workspaceId == workspaceId)
            .Select(m => m.UserId);

        await context.EmailVerificationTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.WorkspaceRegistrationTokens
            .Where(t => t.WorkspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.PasswordResetTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Invitations
            .Where(i => i.workspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.WorkspaceModuleProvisions
            .Where(p => p.workspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Roles
            .Where(r => r.workspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.WorkspaceMemberships
            .Where(m => m.workspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Workspaces
            .Where(o => o.Id == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
