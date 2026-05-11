using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.DataModeling.Infrastructure.Repositories;

internal sealed class DataModelRepository(DataModelingDbContext context) : IDataModelRepository
{
    public async Task AddAsync(DataModel model, CancellationToken ct = default)
        => await context.DataModels.AddAsync(model, ct);

    public async Task<DataModel?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.DataModels
            .FirstOrDefaultAsync(m => m.Id == id && m.OrganizationId == organizationId && !m.IsDeleted, ct);

    public async Task<IReadOnlyList<DataModel>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
        => await context.DataModels
            .Where(m => m.OrganizationId == organizationId && !m.IsDeleted)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DataModel> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<DataModel> query = context.DataModels
            .Where(m => m.OrganizationId == organizationId && !m.IsDeleted)
            .OrderBy(m => m.Name);

        int totalCount = await query.CountAsync(ct);
        List<DataModel> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> NameExistsAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken ct = default)
        => await context.DataModels
            .AnyAsync(m => m.OrganizationId == organizationId
                && !m.IsDeleted
                && m.Name.ToLower() == name.ToLower()
                && (excludeId == null || m.Id != excludeId), ct);
}
