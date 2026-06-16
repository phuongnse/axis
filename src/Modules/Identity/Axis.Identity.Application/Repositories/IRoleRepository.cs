using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IRoleRepository
{
    Task AddAsync(Role role, CancellationToken ct = default);
    Task<Role?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default);
    Task<Role?> GetByNameAsync(string name, Guid teamAccountId, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(Guid teamAccountId, CancellationToken ct = default);
    Task<(IReadOnlyList<Role> Items, int TotalCount)> GetPagedAsync(Guid teamAccountId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid teamAccountId, Guid? excludeRoleId = null, CancellationToken ct = default);

    /// <summary>Loads multiple roles by their IDs — used to resolve user permissions for JWT claims.</summary>
    Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<Guid> ids, Guid teamAccountId, CancellationToken ct = default);
}
