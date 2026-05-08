using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataClassRepository(DataModelingDbContext context) : IDataClassRepository
{
    public async Task AddAsync(DataClass dataClass, CancellationToken ct = default)
        => await context.DataClasses.AddAsync(dataClass, ct);

    public async Task<DataClass?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.DataClasses
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == organizationId && !c.IsDeleted, ct);

    public async Task<IReadOnlyList<DataClass>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
        => await context.DataClasses
            .Where(c => c.OrganizationId == organizationId && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<bool> NameExistsAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken ct = default)
        => await context.DataClasses
            .AnyAsync(c => c.OrganizationId == organizationId
                && !c.IsDeleted
                && c.Name.ToLower() == name.ToLower()
                && (excludeId == null || c.Id != excludeId), ct);

    public async Task<bool> IsReferencedByAnyModelAsync(Guid dataClassId, CancellationToken ct = default)
    {
        var jsonContains = $"[{{\"type\":\"DataClass\",\"config\":{{\"dataClassId\":\"{dataClassId:D}\"}}}}]";
        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT CAST(COUNT(*) AS int) AS \"Value\" FROM data_models WHERE NOT is_deleted AND fields @> {0}::jsonb",
                jsonContains)
            .FirstAsync(ct);
        return count > 0;
    }
}
