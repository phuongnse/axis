using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataRecordRepository(DataModelingDbContext context) : IDataRecordRepository
{
    public async Task AddAsync(DataRecord record, CancellationToken ct = default)
        => await context.DataRecords.AddAsync(record, ct);

    public async Task<DataRecord?> GetByIdAsync(Guid id, Guid modelId, Guid organizationId, CancellationToken ct = default)
        => await context.DataRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.ModelId == modelId && r.OrganizationId == organizationId && !r.IsDeleted, ct);

    public async Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid organizationId, CancellationToken ct = default)
        => await context.DataRecords
            .Where(r => r.ModelId == modelId && r.OrganizationId == organizationId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DataRecord> Records, int TotalCount)> GetPagedAsync(
        Guid modelId, Guid organizationId,
        int page, int pageSize,
        string? search,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            var baseQuery = context.DataRecords
                .Where(r => r.ModelId == modelId && r.OrganizationId == organizationId && !r.IsDeleted);

            var total = await baseQuery.CountAsync(ct);
            var records = await baseQuery
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (records, total);
        }
        else
        {
            // data::text ILIKE is the only portable way to search across all JSONB field values
            var pattern = $"%{search}%";
            var ids = await context.Database
                .SqlQueryRaw<Guid>(
                    "SELECT id AS \"Value\" FROM data_records WHERE model_id = {0} AND organization_id = {1} AND NOT is_deleted AND data::text ILIKE {2}",
                    modelId, organizationId, pattern)
                .ToListAsync(ct);

            var total = ids.Count;
            var pageIds = ids.Skip((page - 1) * pageSize).Take(pageSize).ToHashSet();
            var records = await context.DataRecords
                .Where(r => pageIds.Contains(r.Id))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

            return (records, total);
        }
    }
}
