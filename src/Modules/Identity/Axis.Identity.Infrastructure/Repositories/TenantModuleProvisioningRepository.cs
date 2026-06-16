using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TenantModuleProvisioningRepository(IdentityDbContext context)
    : ITenantModuleProvisioningRepository
{
    public async Task AddRangeAsync(IEnumerable<TenantModuleProvisioning> rows, CancellationToken cancellationToken = default)
        => await context.TenantModuleProvisions.AddRangeAsync(rows, cancellationToken);

    public async Task<TenantModuleProvisioning?> GetAsync(
        Guid teamAccountId,
        string module,
        CancellationToken cancellationToken = default)
        => await context.TenantModuleProvisions
            .FirstOrDefaultAsync(
                p => p.TeamAccountId == teamAccountId && p.Module == module,
                cancellationToken);

    public async Task<IReadOnlyList<TenantModuleProvisioning>> GetAllForTeamAccountAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken = default)
        => await context.TenantModuleProvisions
            .Where(p => p.TeamAccountId == teamAccountId)
            .ToListAsync(cancellationToken);
}
