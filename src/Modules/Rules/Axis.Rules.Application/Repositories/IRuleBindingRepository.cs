using Axis.Rules.Domain;

namespace Axis.Rules.Application.Repositories;

public interface IRuleBindingRepository
{
    Task AddAsync(RuleBinding binding, CancellationToken cancellationToken = default);
    Task<RuleBinding?> GetByIdForWorkspaceAsync(RuleBindingId id, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleBinding>> ListByDefinitionAsync(RuleDefinitionKey key, int version, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleBinding>> ListByTargetAsync(string targetType, string targetId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<RuleBinding?> GetByIdentityForWorkspaceAsync(
        Guid workspaceId,
        RuleDefinitionKey definitionKey,
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<RuleBinding?>(null);
    Task<RuleBinding?> GetInstalledByComponentKeyAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<RuleBinding?>(null);
    void Remove(RuleBinding binding);
}
