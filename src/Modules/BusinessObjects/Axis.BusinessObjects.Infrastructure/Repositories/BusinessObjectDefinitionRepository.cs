using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

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

    public async Task<BusinessObjectDefinition?> GetByKeyForWorkspaceAsync(
        BusinessObjectDefinitionKey key,
        Guid workspaceId,
        CancellationToken ct = default) =>
        await DefinitionsWithGraph()
            .FirstOrDefaultAsync(
                definition => definition.Key == key && definition.WorkspaceId == workspaceId,
                ct);

    public async Task<BusinessObjectDefinition?> GetInstalledByComponentKeyAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken ct = default) =>
        await DefinitionsWithGraph()
            .FirstOrDefaultAsync(
                definition => definition.WorkspaceId == workspaceId &&
                    definition.InstalledComponentKey == componentKey,
                ct);

    public async Task<BusinessObjectDefinitionVersion?> GetPublishedVersionByIdForWorkspaceAsync(
        BusinessObjectDefinitionVersionId id,
        Guid workspaceId,
        CancellationToken ct = default) =>
        await context.BusinessObjectDefinitions
            .AsNoTracking()
            .Where(definition => definition.WorkspaceId == workspaceId)
            .SelectMany(definition => definition.Versions)
            .Include(version => version.Fields)
            .ThenInclude(field => field.Rules)
            .Include(version => version.Fields)
            .ThenInclude(field => field.ChoiceOptions)
            .FirstOrDefaultAsync(version => version.Id == id, ct);

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

    public async Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        string? searchQuery = null,
        bool publishedOnly = false,
        CancellationToken ct = default) =>
        await Search(
                FilterVisibility(
                    context.BusinessObjectDefinitions.AsNoTracking()
                        .Where(definition => definition.WorkspaceId == workspaceId),
                    publishedOnly),
                searchQuery)
            .CountAsync(ct);

    public async Task<IReadOnlyList<BusinessObjectDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        string? searchQuery = null,
        bool publishedOnly = false,
        CancellationToken ct = default) =>
        await Order(
                Search(
                    FilterVisibility(
                        context.BusinessObjectDefinitions.AsNoTracking()
                            .Where(definition => definition.WorkspaceId == workspaceId),
                        publishedOnly),
                    searchQuery),
                searchQuery)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    private static IQueryable<BusinessObjectDefinition> FilterVisibility(
        IQueryable<BusinessObjectDefinition> definitions,
        bool publishedOnly) =>
        publishedOnly
            ? definitions.Where(definition => definition.Status == BusinessObjectDefinitionStatus.Published)
            : definitions;

    private static IQueryable<BusinessObjectDefinition> Search(
        IQueryable<BusinessObjectDefinition> definitions,
        string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return definitions;

        string query = searchQuery.Trim().ToLowerInvariant();
        return definitions.Where(definition =>
            EF.Property<NpgsqlTsVector>(definition, "SearchVector")
                .Matches(EF.Functions.WebSearchToTsQuery("simple", EF.Functions.Unaccent(query))) ||
            EF.Functions.TrigramsAreSimilar(
                EF.Property<string>(definition, "SearchTitle"),
                EF.Functions.Unaccent(query)) ||
            EF.Functions.ILike(
                EF.Property<string>(definition, "SearchText"),
                "%" + EF.Functions.Unaccent(query) + "%"));
    }

    private static IOrderedQueryable<BusinessObjectDefinition> Order(
        IQueryable<BusinessObjectDefinition> definitions,
        string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return definitions
                .OrderByDescending(definition => definition.UpdatedAt)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key);
        }

        string query = searchQuery.Trim().ToLowerInvariant();
        return definitions
            .OrderByDescending(definition =>
                EF.Property<string>(definition, "SearchTitle") == EF.Functions.Unaccent(query))
            .ThenByDescending(definition =>
                EF.Property<string>(definition, "SearchTitle").StartsWith(EF.Functions.Unaccent(query)))
            .ThenByDescending(definition =>
                EF.Property<NpgsqlTsVector>(definition, "SearchVector")
                    .RankCoverDensity(
                        EF.Functions.WebSearchToTsQuery("simple", EF.Functions.Unaccent(query))))
            .ThenByDescending(definition =>
                EF.Functions.TrigramsStrictWordSimilarity(
                    EF.Functions.Unaccent(query),
                    EF.Property<string>(definition, "SearchText")))
            .ThenBy(definition => definition.Name)
            .ThenBy(definition => definition.Key);
    }

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
