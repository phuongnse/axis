using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using ContractLifecycleStatus = Axis.Rules.Contracts.RuleLifecycleStatus;
using ContractOrigin = Axis.Rules.Contracts.RuleOrigin;
using ContractScope = Axis.Rules.Contracts.RuleScope;
using DomainLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;
using DomainScope = Axis.Rules.Domain.RuleScope;

namespace Axis.Rules.Application.Queries.ListRuleDefinitions;

public sealed class ListRuleDefinitionsHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository repository)
    : IQueryHandler<ListRuleDefinitionsQuery, Result<PagedResult<RuleDefinitionSummaryDto>>>
{
    public async Task<Result<PagedResult<RuleDefinitionSummaryDto>>> Handle(
        ListRuleDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<PagedResult<RuleDefinitionSummaryDto>>();

        IReadOnlyList<RuleDefinitionSummaryDto> systemDefinitions = query.Origin == ContractOrigin.Workspace
            ? []
            : SystemRuleCatalog.Definitions
                .Where(definition => query.Scope is null || (ContractScope)definition.Scope == query.Scope)
                .Where(definition => query.Status is null || query.Status == ContractLifecycleStatus.Published)
                .OrderBy(definition => definition.DisplayName, StringComparer.Ordinal)
                .ThenBy(definition => definition.Key.Value, StringComparer.Ordinal)
                .Select(RuleContractMapper.ToSummaryDto)
                .ToArray();

        bool includeWorkspace = query.Origin != ContractOrigin.System;
        DomainScope? workspaceScope = query.Scope is null ? null : (DomainScope)query.Scope.Value;
        DomainLifecycleStatus? workspaceStatus = query.Status is null
            ? null
            : (DomainLifecycleStatus)query.Status.Value;
        int workspaceCount = includeWorkspace
            ? await repository.CountForWorkspaceAsync(
                workspaceId,
                workspaceScope,
                workspaceStatus,
                cancellationToken)
            : 0;

        int skip = (query.Page - 1) * query.PageSize;
        List<RuleDefinitionSummaryDto> items = systemDefinitions
            .Skip(skip)
            .Take(query.PageSize)
            .ToList();

        int remaining = query.PageSize - items.Count;
        if (includeWorkspace && remaining > 0)
        {
            int workspaceSkip = Math.Max(0, skip - systemDefinitions.Count);
            IReadOnlyList<RuleDefinition> workspaceDefinitions = await repository.ListForWorkspaceAsync(
                workspaceId,
                workspaceSkip,
                remaining,
                workspaceScope,
                workspaceStatus,
                cancellationToken);
            items.AddRange(workspaceDefinitions.Select(RuleContractMapper.ToSummaryDto));
        }

        return new PagedResult<RuleDefinitionSummaryDto>(
            items,
            systemDefinitions.Count + workspaceCount,
            query.Page,
            query.PageSize);
    }
}
