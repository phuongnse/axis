using Axis.Rules.Application.Repositories;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
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
        CancellationToken cancellationToken = default)
    {
        IQueryable<RuleDefinition> query = Filter(context.RuleDefinitions.AsNoTracking(), workspaceId, status);
        return await Order(Search(query, searchQuery), searchQuery)
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
        if (status is not null)
            query = query.Where(definition => definition.Status == status.Value);
        return query;
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
        string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return definitions
                .OrderBy(definition => definition.Name)
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
}
