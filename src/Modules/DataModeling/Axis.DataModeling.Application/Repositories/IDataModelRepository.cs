using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataModelRepository
{
    Task AddAsync(DataModel model, CancellationToken ct = default);
    Task<DataModel?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<DataModel>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IReadOnlyList<DataModel> Items, int TotalCount)> GetPagedAsync(Guid tenantId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid tenantId, Guid? excludeId = null, CancellationToken ct = default);
}
