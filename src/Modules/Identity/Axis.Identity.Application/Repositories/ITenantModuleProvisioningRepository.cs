using Axis.Identity.Domain.Provisioning;

namespace Axis.Identity.Application.Repositories;

public interface ITenantModuleProvisioningRepository
{
    Task AddRangeAsync(IEnumerable<TenantModuleProvisioning> rows, CancellationToken cancellationToken = default);

    Task<TenantModuleProvisioning?> GetAsync(
        Guid tenantId,
        string module,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantModuleProvisioning>> GetAllForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
