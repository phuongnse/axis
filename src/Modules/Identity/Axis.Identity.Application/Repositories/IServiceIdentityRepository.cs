using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IServiceIdentityRepository
{
    Task AddAsync(ServiceIdentity identity, CancellationToken ct = default);
    Task<ServiceIdentity?> GetAsync(Guid workspaceId, Guid identityId, CancellationToken ct = default);
    Task<ServiceIdentity?> GetByIdAsync(Guid identityId, CancellationToken ct = default);
    Task<ServiceIdentity?> GetByClientIdAsync(string clientId, CancellationToken ct = default);
    Task<bool> ClientIdExistsAsync(string clientId, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceIdentity>> ListAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ServiceIdentity>>([]);
}
