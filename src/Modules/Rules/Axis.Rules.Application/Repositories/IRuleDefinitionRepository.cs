using Axis.Rules.Domain;
using Axis.Shared.Application;

namespace Axis.Rules.Application.Repositories;

public interface IRuleDefinitionRepository
{
    Task AddAsync(RuleDefinition definition, CancellationToken cancellationToken = default);
    Task<RuleDefinition?> GetByKeyForWorkspaceAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleDefinition>> ListByKeysForWorkspaceAsync(
        IReadOnlyList<RuleDefinitionKey> keys,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
    Task<bool> KeyExistsAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
    Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        RuleLifecycleStatus? status = null,
        string? searchQuery = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int skip,
        int take,
        RuleLifecycleStatus? status = null,
        string? searchQuery = null,
        RuleDefinitionSortField? sortBy = null,
        CollectionSortDirection? sortDirection = null,
        CancellationToken cancellationToken = default);
}
