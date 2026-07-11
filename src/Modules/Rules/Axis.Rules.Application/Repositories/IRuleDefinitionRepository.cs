using Axis.Rules.Domain;

namespace Axis.Rules.Application.Repositories;

public interface IRuleDefinitionRepository
{
    Task AddAsync(RuleDefinition definition, CancellationToken cancellationToken = default);
    Task<RuleDefinition?> GetByKeyForWorkspaceAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
    Task<bool> KeyExistsAsync(
        RuleDefinitionKey key,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
    Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        RuleScope? scope = null,
        RuleLifecycleStatus? status = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int skip,
        int take,
        RuleScope? scope = null,
        RuleLifecycleStatus? status = null,
        CancellationToken cancellationToken = default);
}
