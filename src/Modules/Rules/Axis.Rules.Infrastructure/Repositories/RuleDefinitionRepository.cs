using Axis.Rules.Application;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Shared.Application;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Axis.Rules.Infrastructure.Repositories;

internal sealed class RuleDefinitionRepository(RulesDbContext context) : IRuleDefinitionRepository
{
    public async Task AddAsync(
        RuleDefinition definition,
        CancellationToken cancellationToken = default) =>
        await context.RuleDefinitions.AddAsync(definition, cancellationToken);

    public async Task<RuleDefinition?> GetByKeyForWorkspaceAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await context.RuleDefinitions
            .Include(definition => definition.Versions)
            .FirstOrDefaultAsync(
                definition => definition.WorkspaceId == workspaceId && definition.Key == key,
                cancellationToken);

    public async Task<IReadOnlyList<RuleDefinition>> ListByKeysForWorkspaceAsync(
        IReadOnlyList<RuleDefinitionKey> keys,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
            return [];

        return await context.RuleDefinitions
            .AsNoTracking()
            .Where(definition =>
                definition.WorkspaceId == workspaceId &&
                keys.Contains(definition.Key))
            .OrderBy(definition => definition.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> KeyExistsAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await context.RuleDefinitions.AnyAsync(
            definition => definition.WorkspaceId == workspaceId && definition.Key == key,
            cancellationToken);

    public async Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        RuleLifecycleStatus? status = null,
        string? searchQuery = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<RuleDefinition> query = Filter(context.RuleDefinitions.AsNoTracking(), workspaceId, status);
        return await Search(query, searchQuery).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int skip,
        int take,
        RuleLifecycleStatus? status = null,
        string? searchQuery = null,
        RuleDefinitionSortField? sortBy = null,
        CollectionSortDirection? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<RuleDefinition> query = Filter(context.RuleDefinitions.AsNoTracking(), workspaceId, status);
        return await Order(Search(query, searchQuery), searchQuery, sortBy, sortDirection)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RuleDefinition> Filter(
        IQueryable<RuleDefinition> query,
        Guid workspaceId,
        RuleLifecycleStatus? status)
    {
        query = query.Where(definition => definition.WorkspaceId == workspaceId);
        return status switch
        {
            RuleLifecycleStatus.Draft => query.Where(definition =>
                definition.ArchivedAt == null && definition.LatestPublishedVersion == null),
            RuleLifecycleStatus.Inactive => query.Where(definition =>
                definition.ArchivedAt == null &&
                definition.LatestPublishedVersion != null &&
                definition.ActiveVersion == null),
            RuleLifecycleStatus.Active => query.Where(definition =>
                definition.ArchivedAt == null && definition.ActiveVersion != null),
            RuleLifecycleStatus.Archived => query.Where(definition => definition.ArchivedAt != null),
            _ => query,
        };
    }

    private static IQueryable<RuleDefinition> Search(
        IQueryable<RuleDefinition> definitions,
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

    private static IOrderedQueryable<RuleDefinition> Order(
        IQueryable<RuleDefinition> definitions,
        string? searchQuery,
        RuleDefinitionSortField? sortBy,
        CollectionSortDirection? sortDirection)
    {
        if ((sortBy is null) != (sortDirection is null))
            throw new ArgumentException("A rule definition sort field and direction must be provided together.");

        if (sortBy == RuleDefinitionSortField.Name)
        {
            return sortDirection switch
            {
                CollectionSortDirection.Ascending => definitions
                    .OrderBy(definition => EF.Functions.Collate(definition.Name, "C"))
                    .ThenBy(definition => definition.Key)
                    .ThenBy(definition => definition.Id),
                CollectionSortDirection.Descending => definitions
                    .OrderByDescending(definition => EF.Functions.Collate(definition.Name, "C"))
                    .ThenBy(definition => definition.Key)
                    .ThenBy(definition => definition.Id),
                _ => throw new ArgumentOutOfRangeException(nameof(sortDirection)),
            };
        }

        if (sortBy == RuleDefinitionSortField.Origin)
        {
            return definitions
                .OrderBy(definition => EF.Functions.Collate(definition.Name, "C"))
                .ThenBy(definition => definition.Key)
                .ThenBy(definition => definition.Id);
        }

        if (sortBy == RuleDefinitionSortField.Status)
        {
            return sortDirection switch
            {
                CollectionSortDirection.Ascending => definitions
                    .OrderBy(definition => definition.ArchivedAt != null
                        ? "Archived"
                        : definition.ActiveVersion != null
                            ? "Active"
                            : definition.LatestPublishedVersion != null
                                ? "Inactive"
                                : "Draft")
                    .ThenBy(definition => EF.Functions.Collate(definition.Name, "C"))
                    .ThenBy(definition => definition.Key)
                    .ThenBy(definition => definition.Id),
                CollectionSortDirection.Descending => definitions
                    .OrderByDescending(definition => definition.ArchivedAt != null
                        ? "Archived"
                        : definition.ActiveVersion != null
                            ? "Active"
                            : definition.LatestPublishedVersion != null
                                ? "Inactive"
                                : "Draft")
                    .ThenBy(definition => EF.Functions.Collate(definition.Name, "C"))
                    .ThenBy(definition => definition.Key)
                    .ThenBy(definition => definition.Id),
                _ => throw new ArgumentOutOfRangeException(nameof(sortDirection)),
            };
        }

        if (sortBy == RuleDefinitionSortField.ActiveVersion)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => definition.ActiveVersion == null).ThenBy(definition => definition.ActiveVersion).ThenBy(definition => definition.Id)
                : definitions.OrderBy(definition => definition.ActiveVersion == null).ThenByDescending(definition => definition.ActiveVersion).ThenBy(definition => definition.Id);

        if (sortBy == RuleDefinitionSortField.LatestVersion)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => definition.LatestPublishedVersion == null).ThenBy(definition => definition.LatestPublishedVersion).ThenBy(definition => definition.Id)
                : definitions.OrderBy(definition => definition.LatestPublishedVersion == null).ThenByDescending(definition => definition.LatestPublishedVersion).ThenBy(definition => definition.Id);

        if (sortBy == RuleDefinitionSortField.Revision)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => definition.Revision).ThenBy(definition => definition.Id)
                : definitions.OrderByDescending(definition => definition.Revision).ThenBy(definition => definition.Id);

        if (sortBy == RuleDefinitionSortField.CreatedBy)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => EF.Property<string>(definition, "CreatedByActorDisplayName") == null).ThenBy(definition => EF.Property<string>(definition, "CreatedByActorDisplayName")).ThenBy(definition => definition.Id)
                : definitions.OrderBy(definition => EF.Property<string>(definition, "CreatedByActorDisplayName") == null).ThenByDescending(definition => EF.Property<string>(definition, "CreatedByActorDisplayName")).ThenBy(definition => definition.Id);

        if (sortBy == RuleDefinitionSortField.CreatedAt)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => definition.CreatedAt).ThenBy(definition => definition.Id)
                : definitions.OrderByDescending(definition => definition.CreatedAt).ThenBy(definition => definition.Id);

        if (sortBy == RuleDefinitionSortField.ModifiedBy)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => EF.Property<string>(definition, "UpdatedByActorDisplayName") == null).ThenBy(definition => EF.Property<string>(definition, "UpdatedByActorDisplayName")).ThenBy(definition => definition.Id)
                : definitions.OrderBy(definition => EF.Property<string>(definition, "UpdatedByActorDisplayName") == null).ThenByDescending(definition => EF.Property<string>(definition, "UpdatedByActorDisplayName")).ThenBy(definition => definition.Id);

        if (sortBy == RuleDefinitionSortField.ModifiedAt)
            return sortDirection == CollectionSortDirection.Ascending
                ? definitions.OrderBy(definition => definition.UpdatedAt).ThenBy(definition => definition.Id)
                : definitions.OrderByDescending(definition => definition.UpdatedAt).ThenBy(definition => definition.Id);

        if (sortBy is not null)
            throw new ArgumentOutOfRangeException(nameof(sortBy));

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return definitions
                .OrderBy(definition => definition.Name)
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
}
