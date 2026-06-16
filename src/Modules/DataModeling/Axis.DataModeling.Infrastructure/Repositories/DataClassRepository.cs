using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataClassRepository(DataModelingDbContext context) : IDataClassRepository
{
    public async Task AddAsync(DataClass dataClass, CancellationToken ct = default)
        => await context.DataClasses.AddAsync(dataClass, ct);

    public async Task<DataClass?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        => await context.DataClasses
            .FirstOrDefaultAsync(c => c.Id == id && c.tenantId == tenantId, ct);

    public async Task<IReadOnlyList<DataClass>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
        => await context.DataClasses
            .Where(c => c.tenantId == tenantId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DataClass> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<DataClass> query = context.DataClasses
            .Where(c => c.tenantId == tenantId)
            .OrderBy(c => c.Name);

        int totalCount = await query.CountAsync(ct);
        List<DataClass> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> NameExistsAsync(string name, Guid tenantId, Guid? excludeId = null, CancellationToken ct = default)
        => await context.DataClasses
            .AnyAsync(c => c.tenantId == tenantId
                && c.Name.ToLower() == name.ToLower()
                && (excludeId == null || c.Id != excludeId), ct);

    public async Task<bool> IsReferencedByAnyModelAsync(Guid dataClassId, CancellationToken ct = default)
    {
        string jsonContains = $"[{{\"type\":\"DataClass\",\"config\":{{\"dataClassId\":\"{dataClassId:D}\"}}}}]";
        int count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT CAST(COUNT(*) AS int) AS \"Value\" FROM data_models WHERE deleted_at IS NULL AND fields @> {0}::jsonb",
                jsonContains)
            .FirstAsync(ct);
        return count > 0;
    }
}
