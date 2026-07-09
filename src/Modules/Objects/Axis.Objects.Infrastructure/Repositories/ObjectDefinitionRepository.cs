using Axis.Objects.Application.Repositories;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Objects.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Objects.Infrastructure.Repositories;

internal sealed class ObjectDefinitionRepository(ObjectsDbContext context) : IObjectDefinitionRepository
{
    public async Task AddAsync(ObjectDefinition definition, CancellationToken ct = default) =>
        await context.ObjectDefinitions.AddAsync(definition, ct);

    public async Task<ObjectDefinition?> GetByIdForWorkspaceAsync(
        ObjectDefinitionId id,
        Guid workspaceId,
        CancellationToken ct = default) =>
        await DefinitionsWithGraph()
            .FirstOrDefaultAsync(
                definition => definition.Id == id && definition.WorkspaceId == workspaceId,
                ct);

    public async Task<bool> ObjectKeyExistsAsync(
        Guid workspaceId,
        ObjectDefinitionKey key,
        ObjectDefinitionId? exceptId = null,
        CancellationToken ct = default)
    {
        IQueryable<ObjectDefinition> query = context.ObjectDefinitions
            .Where(definition => definition.WorkspaceId == workspaceId && definition.Key == key);

        if (exceptId.HasValue)
            query = query.Where(definition => definition.Id != exceptId.Value);

        return await query.AnyAsync(ct);
    }

    public async Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        await context.ObjectDefinitions
            .AsNoTracking()
            .CountAsync(definition => definition.WorkspaceId == workspaceId, ct);

    public async Task<IReadOnlyList<ObjectDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        CancellationToken ct = default) =>
        await context.ObjectDefinitions
            .AsNoTracking()
            .Where(definition => definition.WorkspaceId == workspaceId)
            .OrderByDescending(definition => definition.UpdatedAt)
            .ThenBy(definition => definition.Name)
            .ThenBy(definition => definition.Key)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    private IQueryable<ObjectDefinition> DefinitionsWithGraph() =>
        context.ObjectDefinitions
            .AsSplitQuery()
            .Include(definition => definition.Fields)
            .ThenInclude(field => field.Variants)
            .Include(definition => definition.Versions)
            .ThenInclude(version => version.Fields)
            .ThenInclude(field => field.Variants);
}
