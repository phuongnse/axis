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
}
