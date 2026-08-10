using Axis.Rules.Application.Repositories;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Rules.Infrastructure.Repositories;

internal sealed class RuleBindingRepository(RulesDbContext context) : IRuleBindingRepository
{
    public async Task AddAsync(RuleBinding binding, CancellationToken cancellationToken = default) =>
        await context.RuleBindings.AddAsync(binding, cancellationToken);

    public Task<RuleBinding?> GetByIdForWorkspaceAsync(
        RuleBindingId id,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        context.RuleBindings.FirstOrDefaultAsync(
            binding => binding.Id == id && binding.WorkspaceId == workspaceId,
            cancellationToken);

    public async Task<IReadOnlyList<RuleBinding>> ListByDefinitionAsync(
        RuleDefinitionKey key,
        int version,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await context.RuleBindings
            .AsNoTracking()
            .Where(binding => binding.WorkspaceId == workspaceId &&
                              binding.DefinitionKey == key &&
                              binding.DefinitionVersion == version)
            .OrderBy(binding => binding.Priority)
            .ThenBy(binding => binding.TargetType)
            .ThenBy(binding => binding.TargetId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RuleBinding>> ListByTargetAsync(
        string targetType,
        string targetId,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await context.RuleBindings
            .AsNoTracking()
            .Where(binding => binding.WorkspaceId == workspaceId &&
                              binding.TargetType == targetType &&
                              binding.TargetId == targetId)
            .OrderBy(binding => binding.Priority)
            .ThenBy(binding => binding.UseCaseOrTrigger)
            .ToListAsync(cancellationToken);

    public Task<RuleBinding?> GetByIdentityForWorkspaceAsync(
        Guid workspaceId,
        RuleDefinitionKey definitionKey,
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        CancellationToken cancellationToken = default) =>
        context.RuleBindings.SingleOrDefaultAsync(
            binding => binding.WorkspaceId == workspaceId &&
                binding.DefinitionKey == definitionKey &&
                binding.DefinitionVersion == definitionVersion &&
                binding.TargetType == targetType &&
                binding.TargetId == targetId &&
                binding.UseCaseOrTrigger == useCaseOrTrigger,
            cancellationToken);

    public Task<RuleBinding?> GetInstalledByComponentKeyAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken cancellationToken = default) =>
        context.RuleBindings.SingleOrDefaultAsync(
            binding => binding.WorkspaceId == workspaceId &&
                binding.InstalledComponentKey == componentKey,
            cancellationToken);

    public void Remove(RuleBinding binding) => context.RuleBindings.Remove(binding);
}
