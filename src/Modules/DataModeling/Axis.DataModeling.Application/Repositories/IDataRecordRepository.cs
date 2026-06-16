using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Domain.Aggregates;

namespace Axis.DataModeling.Application.Repositories;

public interface IDataRecordRepository
{
    Task AddAsync(DataRecord record, CancellationToken ct = default);
    Task<DataRecord?> GetByIdAsync(Guid id, Guid modelId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated page of records with optional full-text search,
    /// per-field filters, and sort.
    /// </summary>
    Task<(IReadOnlyList<DataRecord> Records, int TotalCount)> GetPagedAsync(
        Guid modelId,
        Guid tenantId,
        int page,
        int pageSize,
        string? search = null,
        IReadOnlyList<RecordFilter>? filters = null,
        string? sortBy = null,
        string? sortDir = null,
        CancellationToken ct = default);

    /// <summary>Soft-deletes all records matching the given IDs. Returns count actually deleted.</summary>
    Task<int> BulkDeleteAsync(
        IReadOnlyList<Guid> ids,
        Guid modelId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>Streams all matching records for CSV export without loading into memory at once.</summary>
    IAsyncEnumerable<DataRecord> GetAllForExportAsync(
        Guid modelId,
        Guid tenantId,
        string? search = null,
        IReadOnlyList<RecordFilter>? filters = null,
        string? sortBy = null,
        string? sortDir = null,
        CancellationToken ct = default);
}
