using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IRoleRepository
{
    Task AddAsync(Role role, CancellationToken ct = default);
    Task<Role?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<Role?> GetByNameAsync(string name, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(Guid organizationId, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid organizationId, Guid? excludeRoleId = null, CancellationToken ct = default);
}
