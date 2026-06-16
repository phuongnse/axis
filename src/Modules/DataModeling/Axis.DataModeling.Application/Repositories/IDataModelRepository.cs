using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataModelRepository
{
    Task AddAsync(DataModel model, CancellationToken ct = default);
    Task<DataModel?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default);
    Task<IReadOnlyList<DataModel>> GetAllAsync(Guid teamAccountId, CancellationToken ct = default);
    Task<(IReadOnlyList<DataModel> Items, int TotalCount)> GetPagedAsync(Guid teamAccountId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid teamAccountId, Guid? excludeId = null, CancellationToken ct = default);
}
