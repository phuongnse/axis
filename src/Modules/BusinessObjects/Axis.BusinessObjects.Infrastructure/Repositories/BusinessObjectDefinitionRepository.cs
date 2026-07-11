using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.BusinessObjects.Infrastructure.Repositories;

internal sealed class BusinessObjectDefinitionRepository(BusinessObjectsDbContext context) : IBusinessObjectDefinitionRepository
{
    public async Task AddAsync(BusinessObjectDefinition definition, CancellationToken ct = default) =>
        await context.BusinessObjectDefinitions.AddAsync(definition, ct);

    public async Task<BusinessObjectDefinition?> GetByIdForWorkspaceAsync(
        BusinessObjectDefinitionId id,
        Guid workspaceId,
        CancellationToken ct = default) =>
        await DefinitionsWithGraph()
            .FirstOrDefaultAsync(
                definition => definition.Id == id && definition.WorkspaceId == workspaceId,
                ct);

    public async Task<bool> ObjectKeyExistsAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey key,
        BusinessObjectDefinitionId? exceptId = null,
        CancellationToken ct = default)
    {
        IQueryable<BusinessObjectDefinition> query = context.BusinessObjectDefinitions
            .Where(definition => definition.WorkspaceId == workspaceId && definition.Key == key);

        if (exceptId.HasValue)
            query = query.Where(definition => definition.Id != exceptId.Value);

        return await query.AnyAsync(ct);
    }

    public async Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        await context.BusinessObjectDefinitions
            .AsNoTracking()
            .CountAsync(definition => definition.WorkspaceId == workspaceId, ct);

    public async Task<IReadOnlyList<BusinessObjectDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        CancellationToken ct = default) =>
        await context.BusinessObjectDefinitions
            .AsNoTracking()
            .Where(definition => definition.WorkspaceId == workspaceId)
            .OrderByDescending(definition => definition.UpdatedAt)
            .ThenBy(definition => definition.Name)
            .ThenBy(definition => definition.Key)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    private IQueryable<BusinessObjectDefinition> DefinitionsWithGraph() =>
        context.BusinessObjectDefinitions
            .AsSplitQuery()
            .Include(definition => definition.Fields)
            .ThenInclude(field => field.Rules)
            .Include(definition => definition.Fields)
            .ThenInclude(field => field.ChoiceOptions)
            .Include(definition => definition.Versions
                .OrderByDescending(version => version.VersionNumber)
                .Take(1))
            .ThenInclude(version => version.Fields)
            .ThenInclude(field => field.Rules)
            .Include(definition => definition.Versions
                .OrderByDescending(version => version.VersionNumber)
                .Take(1))
            .ThenInclude(version => version.Fields)
            .ThenInclude(field => field.ChoiceOptions);
}
