using Axis.Identity.Domain.Provisioning;

namespace Axis.Identity.Application.Repositories;

public interface ITenantModuleProvisioningRepository
{
    Task AddRangeAsync(IEnumerable<TenantModuleProvisioning> rows, CancellationToken cancellationToken = default);

    Task<TenantModuleProvisioning?> GetAsync(
        Guid teamAccountId,
        string module,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantModuleProvisioning>> GetAllForTeamAccountAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken = default);
}
