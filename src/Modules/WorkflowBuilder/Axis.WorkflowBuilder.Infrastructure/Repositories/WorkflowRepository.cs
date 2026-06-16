using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowBuilder.Infrastructure.Repositories;

internal sealed class WorkflowRepository(WorkflowBuilderDbContext context) : IWorkflowRepository
{
    public async Task AddAsync(WorkflowDefinition workflow, CancellationToken ct = default)
        => await context.WorkflowDefinitions.AddAsync(workflow, ct);

    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default)
        => await context.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Id == id && w.TeamAccountId == teamAccountId, ct);

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(Guid teamAccountId, CancellationToken ct = default)
        => await context.WorkflowDefinitions
            .Where(w => w.TeamAccountId == teamAccountId)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<WorkflowDefinition> Items, int TotalCount)> GetPagedAsync(
        Guid teamAccountId, int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<WorkflowDefinition> query = context.WorkflowDefinitions
            .Where(w => w.TeamAccountId == teamAccountId)
            .OrderByDescending(w => w.UpdatedAt);

        int totalCount = await query.CountAsync(ct);
        List<WorkflowDefinition> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> NameExistsAsync(string name, Guid teamAccountId, Guid? excludeId = null, CancellationToken ct = default)
        => await context.WorkflowDefinitions
            .AnyAsync(w => w.TeamAccountId == teamAccountId
                && w.Name.ToLower() == name.ToLower()
                && (excludeId == null || w.Id != excludeId), ct);

    public Task<int> CountByTeamAccountAsync(Guid teamAccountId, CancellationToken ct = default) =>
        context.WorkflowDefinitions.CountAsync(w => w.TeamAccountId == teamAccountId, ct);
}
