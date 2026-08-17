using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.Shared.Application;
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
        BusinessObjectDefinitionSortField? sortBy = null,
        CollectionSortDirection? sortDirection = null,
        CancellationToken ct = default) =>
        await Order(
                Search(
                    FilterVisibility(
                        context.BusinessObjectDefinitions.AsNoTracking()
                            .Where(definition => definition.WorkspaceId == workspaceId),
                        publishedOnly),
                    searchQuery),
                searchQuery,
                sortBy,
                sortDirection)
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
        string? searchQuery,
        BusinessObjectDefinitionSortField? sortBy,
        CollectionSortDirection? sortDirection)
    {
        if (sortBy.HasValue && sortDirection.HasValue)
            return OrderExplicitly(definitions, sortBy.Value, sortDirection.Value);

        if (sortBy.HasValue || sortDirection.HasValue)
            throw new ArgumentException("Definition sort field and direction must be supplied together.");

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return definitions
                .OrderByDescending(definition => definition.UpdatedAt)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id);
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
            .ThenBy(definition => definition.Key)
            .ThenBy(definition => definition.Id);
    }

    private static IOrderedQueryable<BusinessObjectDefinition> OrderExplicitly(
        IQueryable<BusinessObjectDefinition> definitions,
        BusinessObjectDefinitionSortField sortBy,
        CollectionSortDirection sortDirection) =>
        (sortBy, sortDirection) switch
        {
            (BusinessObjectDefinitionSortField.Name, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Name, CollectionSortDirection.Descending) => definitions
                .OrderByDescending(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Key, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.Key)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Key, CollectionSortDirection.Descending) => definitions
                .OrderByDescending(definition => definition.Key)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Status, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.Status)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Status, CollectionSortDirection.Descending) => definitions
                .OrderByDescending(definition => definition.Status)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Version, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.LatestPublishedVersionNumber == null)
                .ThenBy(definition => definition.LatestPublishedVersionNumber)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Version, CollectionSortDirection.Descending) => definitions
                .OrderBy(definition => definition.LatestPublishedVersionNumber == null)
                .ThenByDescending(definition => definition.LatestPublishedVersionNumber)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Revision, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.Revision)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.Revision, CollectionSortDirection.Descending) => definitions
                .OrderByDescending(definition => definition.Revision)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.CreatedBy, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => EF.Property<string>(definition, "CreatedByDisplayName") == null)
                .ThenBy(definition => EF.Property<string>(definition, "CreatedByDisplayName"))
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.CreatedBy, CollectionSortDirection.Descending) => definitions
                .OrderBy(definition => EF.Property<string>(definition, "CreatedByDisplayName") == null)
                .ThenByDescending(definition => EF.Property<string>(definition, "CreatedByDisplayName"))
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.CreatedAt, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.CreatedAt)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.CreatedAt, CollectionSortDirection.Descending) => definitions
                .OrderByDescending(definition => definition.CreatedAt)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.ModifiedBy, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => EF.Property<string>(definition, "UpdatedByDisplayName") == null)
                .ThenBy(definition => EF.Property<string>(definition, "UpdatedByDisplayName"))
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.ModifiedBy, CollectionSortDirection.Descending) => definitions
                .OrderBy(definition => EF.Property<string>(definition, "UpdatedByDisplayName") == null)
                .ThenByDescending(definition => EF.Property<string>(definition, "UpdatedByDisplayName"))
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.ModifiedAt, CollectionSortDirection.Ascending) => definitions
                .OrderBy(definition => definition.UpdatedAt)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            (BusinessObjectDefinitionSortField.ModifiedAt, CollectionSortDirection.Descending) => definitions
                .OrderByDescending(definition => definition.UpdatedAt)
                .ThenBy(definition => definition.Name)
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, "Definition sort is invalid."),
        };

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
