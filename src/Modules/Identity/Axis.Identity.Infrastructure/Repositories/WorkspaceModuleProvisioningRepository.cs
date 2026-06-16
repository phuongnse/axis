using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceModuleProvisioningRepository(IdentityDbContext context)
    : IWorkspaceModuleProvisioningRepository
{
    public async Task AddRangeAsync(IEnumerable<WorkspaceModuleProvisioning> rows, CancellationToken cancellationToken = default)
        => await context.WorkspaceModuleProvisions.AddRangeAsync(rows, cancellationToken);

    public async Task<WorkspaceModuleProvisioning?> GetAsync(
        Guid workspaceId,
        string module,
        CancellationToken cancellationToken = default)
        => await context.WorkspaceModuleProvisions
            .FirstOrDefaultAsync(
                p => p.workspaceId == workspaceId && p.Module == module,
                cancellationToken);

    public async Task<IReadOnlyList<WorkspaceModuleProvisioning>> GetAllForWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
        => await context.WorkspaceModuleProvisions
            .Where(p => p.workspaceId == workspaceId)
            .ToListAsync(cancellationToken);
}
