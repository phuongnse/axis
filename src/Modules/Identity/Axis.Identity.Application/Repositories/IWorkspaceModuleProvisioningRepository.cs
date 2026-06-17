using Axis.Identity.Domain.Provisioning;

namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceModuleProvisioningRepository
{
    Task AddRangeAsync(IEnumerable<WorkspaceModuleProvisioning> rows, CancellationToken cancellationToken = default);

    Task<WorkspaceModuleProvisioning?> GetAsync(
        Guid workspaceId,
        string module,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceModuleProvisioning>> GetAllForWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}
