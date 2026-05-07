using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataRecordRepository
{
    Task AddAsync(DataRecord record, CancellationToken ct = default);
    Task<DataRecord?> GetByIdAsync(Guid id, Guid modelId, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid organizationId, CancellationToken ct = default);
}
