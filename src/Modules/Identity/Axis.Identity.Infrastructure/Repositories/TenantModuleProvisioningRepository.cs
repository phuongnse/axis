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
        Guid tenantId,
        string module,
        CancellationToken cancellationToken = default)
        => await context.TenantModuleProvisions
            .FirstOrDefaultAsync(
                p => p.tenantId == tenantId && p.Module == module,
                cancellationToken);

    public async Task<IReadOnlyList<TenantModuleProvisioning>> GetAllForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => await context.TenantModuleProvisions
            .Where(p => p.tenantId == tenantId)
            .ToListAsync(cancellationToken);
}
