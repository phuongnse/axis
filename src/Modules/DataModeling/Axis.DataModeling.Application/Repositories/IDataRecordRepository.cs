using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataRecordRepository
{
    Task AddAsync(DataRecord record, CancellationToken ct = default);
    Task<DataRecord?> GetByIdAsync(Guid id, Guid modelId, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated page of records. Optionally performs a full-text search
    /// across all field values via JSONB text cast (US-043).
    /// </summary>
    Task<(IReadOnlyList<DataRecord> Records, int TotalCount)> GetPagedAsync(
        Guid modelId, Guid organizationId,
        int page, int pageSize,
        string? search,
        CancellationToken ct = default);
}
