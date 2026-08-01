using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Search;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using ContractLifecycleStatus = Axis.Rules.Contracts.RuleLifecycleStatus;
using ContractOrigin = Axis.Rules.Contracts.RuleOrigin;
using DomainLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;

namespace Axis.Rules.Application.Queries.ListRuleDefinitions;

public sealed class ListRuleDefinitionsHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository repository,
    IRuleCatalogSearchProvider catalogSearch)
    : IQueryHandler<ListRuleDefinitionsQuery, Result<PagedResult<RuleDefinitionSummaryDto>>>
{
    public async Task<Result<PagedResult<RuleDefinitionSummaryDto>>> Handle(
        ListRuleDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<PagedResult<RuleDefinitionSummaryDto>>();

        IReadOnlyList<RuleDefinition> systemCandidates = query.Origin == ContractOrigin.Workspace
            ? []
            : SystemRuleCatalog.Definitions
                .Where(definition => query.Status is null || query.Status == ContractLifecycleStatus.Published)
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ThenBy(definition => definition.Key.Value, StringComparer.Ordinal)
                .ToArray();

        bool includeWorkspace = query.Origin != ContractOrigin.System;
        DomainLifecycleStatus? workspaceStatus = query.Status is null
            ? null
            : (DomainLifecycleStatus)query.Status.Value;

        if (!string.IsNullOrWhiteSpace(query.SearchQuery))
        {
            int searchSkip = (query.Page - 1) * query.PageSize;
            RuleCatalogSearchPage searchPage = await catalogSearch.SearchAsync(
                workspaceId,
                CreateSearchDocuments(systemCandidates, query.Language),
                includeWorkspace,
                workspaceStatus,
                searchSkip,
                query.PageSize,
                query.SearchQuery,
                cancellationToken);
            IReadOnlyList<RuleDefinitionSummaryDto> searchItems = await HydrateSearchItemsAsync(
                searchPage.Items,
                systemCandidates,
                workspaceId,
                repository,
                cancellationToken);

            return new PagedResult<RuleDefinitionSummaryDto>(
                searchItems,
                searchPage.TotalCount,
                query.Page,
                query.PageSize);
        }

        IReadOnlyList<RuleDefinitionSummaryDto> systemDefinitions =
            systemCandidates.Select(RuleContractMapper.ToSummaryDto).ToArray();
        int workspaceCount = includeWorkspace
            ? await repository.CountForWorkspaceAsync(
                workspaceId,
                workspaceStatus,
                searchQuery: null,
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
                workspaceStatus,
                searchQuery: null,
                cancellationToken);
            items.AddRange(workspaceDefinitions.Select(RuleContractMapper.ToSummaryDto));
        }

        return new PagedResult<RuleDefinitionSummaryDto>(
            items,
            systemDefinitions.Count + workspaceCount,
            query.Page,
            query.PageSize);
    }

    private static IReadOnlyList<RuleTextSearchDocument> CreateSearchDocuments(
        IReadOnlyList<RuleDefinition> definitions,
        string? language)
    {
        string locale = language?.StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true
            ? "vi"
            : "en";
        return definitions
            .Select(definition =>
            {
                RuleReferenceContent content =
                    definition.Documentation!.Locales.TryGetValue(
                        locale,
                        out RuleReferenceContent? localized)
                        ? localized
                        : definition.Documentation!.Locales["en"];
                return new RuleTextSearchDocument(
                    definition.Key.Value,
                    content.DisplayName,
                    $"{content.Summary} {content.Usage} {definition.Key.Value}");
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<RuleDefinitionSummaryDto>> HydrateSearchItemsAsync(
        IReadOnlyList<RuleCatalogSearchItem> searchItems,
        IReadOnlyList<RuleDefinition> systemCandidates,
        Guid workspaceId,
        IRuleDefinitionRepository repository,
        CancellationToken cancellationToken)
    {
        Dictionary<string, RuleDefinition> systemByKey = systemCandidates
            .ToDictionary(definition => definition.Key.Value, StringComparer.Ordinal);
        IReadOnlyList<RuleDefinitionKey> workspaceKeys = searchItems
            .Where(item => item.Origin == Axis.Rules.Domain.RuleOrigin.Workspace)
            .Select(item => RuleDefinitionKey.Create(item.Key))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToArray();
        IReadOnlyList<RuleDefinition> workspaceDefinitions =
            await repository.ListByKeysForWorkspaceAsync(
                workspaceKeys,
                workspaceId,
                cancellationToken);
        Dictionary<string, RuleDefinition> workspaceByKey = workspaceDefinitions
            .ToDictionary(definition => definition.Key.Value, StringComparer.Ordinal);

        List<RuleDefinitionSummaryDto> definitions = [];
        foreach (RuleCatalogSearchItem item in searchItems)
        {
            if (item.Origin == Axis.Rules.Domain.RuleOrigin.System &&
                systemByKey.TryGetValue(item.Key, out RuleDefinition? systemDefinition))
            {
                definitions.Add(RuleContractMapper.ToSummaryDto(systemDefinition));
            }
            else if (item.Origin == Axis.Rules.Domain.RuleOrigin.Workspace &&
                     workspaceByKey.TryGetValue(item.Key, out RuleDefinition? workspaceDefinition))
            {
                definitions.Add(RuleContractMapper.ToSummaryDto(workspaceDefinition));
            }
        }

        return definitions;
    }
}
