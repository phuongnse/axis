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
            .FirstOrDefaultAsync(r => r.Id == id && r.ModelId == modelId && r.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<DataRecord>> GetAllAsync(Guid modelId, Guid organizationId, CancellationToken ct = default)
        => await context.DataRecords
            .Where(r => r.ModelId == modelId && r.OrganizationId == organizationId)
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
            IQueryable<DataRecord> baseQuery = context.DataRecords
                .Where(r => r.ModelId == modelId && r.OrganizationId == organizationId);

            int total = await baseQuery.CountAsync(ct);
            List<DataRecord> records = await baseQuery
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (records, total);
        }
        else
        {
            // data::text ILIKE is the only portable way to search across all JSONB field values
            string pattern = $"%{search}%";
            List<Guid> ids = await context.Database
                .SqlQueryRaw<Guid>(
                    "SELECT id AS \"Value\" FROM data_records WHERE model_id = {0} AND organization_id = {1} AND deleted_at IS NULL AND data::text ILIKE {2}",
                    modelId, organizationId, pattern)
                .ToListAsync(ct);

            int total = ids.Count;
            HashSet<Guid> pageIds = ids.Skip((page - 1) * pageSize).Take(pageSize).ToHashSet();
            List<DataRecord> records = await context.DataRecords
                .Where(r => pageIds.Contains(r.Id))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

            return (records, total);
        }
    }
}
