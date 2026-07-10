using Axis.Rules.Application.Repositories;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

    public async Task<bool> KeyExistsAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await context.RuleDefinitions.AnyAsync(
            definition => definition.WorkspaceId == workspaceId && definition.Key == key,
            cancellationToken);

    public async Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        RuleScope? scope = null,
        RuleLifecycleStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<RuleDefinition> query = Filter(context.RuleDefinitions.AsNoTracking(), workspaceId, scope, status);
        return await query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int skip,
        int take,
        RuleScope? scope = null,
        RuleLifecycleStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<RuleDefinition> query = Filter(context.RuleDefinitions.AsNoTracking(), workspaceId, scope, status);
        return await query
            .OrderBy(definition => definition.Name)
            .ThenBy(definition => definition.Key)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RuleDefinition> Filter(
        IQueryable<RuleDefinition> query,
        Guid workspaceId,
        RuleScope? scope,
        RuleLifecycleStatus? status)
    {
        query = query.Where(definition => definition.WorkspaceId == workspaceId);
        if (scope is not null)
            query = query.Where(definition => definition.Scope == scope.Value);
        if (status is not null)
            query = query.Where(definition => definition.Status == status.Value);
        return query;
    }
}
