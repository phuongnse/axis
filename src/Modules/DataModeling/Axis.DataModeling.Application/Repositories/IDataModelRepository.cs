using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataModelRepository
{
    Task AddAsync(DataModel model, CancellationToken ct = default);
    Task<DataModel?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<DataModel>> GetAllAsync(Guid organizationId, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken ct = default);
}
