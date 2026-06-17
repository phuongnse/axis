using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataClassRepository
{
    Task AddAsync(DataClass dataClass, CancellationToken ct = default);
    Task<DataClass?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<DataClass>> GetAllAsync(Guid workspaceId, CancellationToken ct = default);
    Task<(IReadOnlyList<DataClass> Items, int TotalCount)> GetPagedAsync(Guid workspaceId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid workspaceId, Guid? excludeId = null, CancellationToken ct = default);
    Task<bool> IsReferencedByAnyModelAsync(Guid dataClassId, CancellationToken ct = default);
}
