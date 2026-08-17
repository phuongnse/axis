using Axis.Identity.Contracts;
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
using DomainOrigin = Axis.Rules.Domain.RuleOrigin;

namespace Axis.Rules.Application.Queries.ListRuleDefinitions;

public sealed class ListRuleDefinitionsHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization authorization,
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

        WorkspaceProductBuilderDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<PagedResult<RuleDefinitionSummaryDto>>(decision);

        IReadOnlyList<RuleDefinition> builtInCandidates = query.Origin == ContractOrigin.Workspace
            ? []
            : BuiltInRuleCatalog.Definitions
                .Where(definition => query.Status is null || query.Status == ContractLifecycleStatus.Active)
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ThenBy(definition => definition.Key.Value, StringComparer.Ordinal)
                .ToArray();

        bool includeWorkspace = query.Origin != ContractOrigin.BuiltIn;
        DomainLifecycleStatus? workspaceStatus = query.Status is null
            ? null
            : (DomainLifecycleStatus)query.Status.Value;

        if (!string.IsNullOrWhiteSpace(query.SearchQuery))
        {
            int searchSkip = (query.Page - 1) * query.PageSize;
            RuleCatalogSearchPage searchPage = await catalogSearch.SearchAsync(
                workspaceId,
                CreateSearchDocuments(builtInCandidates, query.Language),
                includeWorkspace,
                workspaceStatus,
                query.SortBy,
                query.SortDirection,
                searchSkip,
                query.PageSize,
                query.SearchQuery,
                cancellationToken);
            IReadOnlyList<RuleDefinitionSummaryDto> searchItems = await HydrateSearchItemsAsync(
                searchPage.Items,
                builtInCandidates,
                workspaceId,
                repository,
                cancellationToken);

            return new PagedResult<RuleDefinitionSummaryDto>(
                searchItems,
                searchPage.TotalCount,
                query.Page,
                query.PageSize);
        }

        IReadOnlyList<RuleDefinitionSummaryDto> builtInDefinitions =
            builtInCandidates.Select(definition => RuleContractMapper.ToSummaryDto(definition)).ToArray();
        int workspaceCount = includeWorkspace
            ? await repository.CountForWorkspaceAsync(
                workspaceId,
                workspaceStatus,
                searchQuery: null,
                cancellationToken)
            : 0;

        int skip = (query.Page - 1) * query.PageSize;
        if (query.SortBy is not null || query.SortDirection is not null)
        {
            return await ListExplicitlySortedAsync(
                query,
                builtInCandidates,
                includeWorkspace,
                workspaceStatus,
                workspaceCount,
                workspaceId,
                skip,
                repository,
                cancellationToken);
        }

        List<RuleDefinitionSummaryDto> items = builtInDefinitions
            .Skip(skip)
            .Take(query.PageSize)
            .ToList();

        int remaining = query.PageSize - items.Count;
        if (includeWorkspace && remaining > 0)
        {
            int workspaceSkip = Math.Max(0, skip - builtInDefinitions.Count);
            IReadOnlyList<RuleDefinition> workspaceDefinitions = await repository.ListForWorkspaceAsync(
                workspaceId,
                workspaceSkip,
                remaining,
                workspaceStatus,
                searchQuery: null,
                cancellationToken: cancellationToken);
            items.AddRange(workspaceDefinitions.Select(definition => RuleContractMapper.ToSummaryDto(definition)));
        }

        return new PagedResult<RuleDefinitionSummaryDto>(
            items,
            builtInDefinitions.Count + workspaceCount,
            query.Page,
            query.PageSize);
    }

    private static async Task<PagedResult<RuleDefinitionSummaryDto>> ListExplicitlySortedAsync(
        ListRuleDefinitionsQuery query,
        IReadOnlyList<RuleDefinition> builtInCandidates,
        bool includeWorkspace,
        DomainLifecycleStatus? workspaceStatus,
        int workspaceCount,
        Guid workspaceId,
        int skip,
        IRuleDefinitionRepository repository,
        CancellationToken cancellationToken)
    {
        if (query.SortBy is null || query.SortDirection is null)
            throw new ArgumentOutOfRangeException(nameof(query.SortBy));

        List<SortableDefinition> candidates = builtInCandidates
            .Select(definition => new SortableDefinition(
                RuleContractMapper.ToSummaryDto(definition),
                DefinitionSortValue(definition, query.Language, query.SortBy.Value),
                DefinitionSortName(definition, query.Language)))
            .ToList();

        if (includeWorkspace && workspaceCount > 0)
        {
            int workspaceTake = Math.Min(workspaceCount, skip + query.PageSize);
            IReadOnlyList<RuleDefinition> workspaceDefinitions = await repository.ListForWorkspaceAsync(
                workspaceId,
                skip: 0,
                take: workspaceTake,
                status: workspaceStatus,
                searchQuery: null,
                sortBy: query.SortBy,
                sortDirection: query.SortDirection,
                cancellationToken: cancellationToken);
            candidates.AddRange(workspaceDefinitions.Select(definition => new SortableDefinition(
                RuleContractMapper.ToSummaryDto(definition),
                DefinitionSortValue(definition, query.Language, query.SortBy.Value),
                definition.Name)));
        }

        IOrderedEnumerable<SortableDefinition> ordered = query.SortDirection switch
        {
            CollectionSortDirection.Ascending => candidates
                .OrderBy(candidate => candidate.SortValue is null)
                .ThenBy(candidate => candidate.SortValue, StringComparer.Ordinal),
            CollectionSortDirection.Descending => candidates
                .OrderBy(candidate => candidate.SortValue is null)
                .ThenByDescending(candidate => candidate.SortValue, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(query.SortDirection)),
        };
        RuleDefinitionSummaryDto[] items = ordered
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Definition.DefinitionKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Definition.Origin)
            .Skip(skip)
            .Take(query.PageSize)
            .Select(candidate => candidate.Definition)
            .ToArray();

        return new PagedResult<RuleDefinitionSummaryDto>(
            items,
            builtInCandidates.Count + workspaceCount,
            query.Page,
            query.PageSize);
    }

    private static string? DefinitionSortValue(
        RuleDefinition definition,
        string? language,
        RuleDefinitionSortField sortBy) =>
        sortBy switch
        {
            RuleDefinitionSortField.Name => DefinitionSortName(definition, language),
            RuleDefinitionSortField.Origin => definition.Origin.ToString(),
            RuleDefinitionSortField.Status => definition.Status.ToString(),
            RuleDefinitionSortField.ActiveVersion => definition.ActiveVersion?.ToString("D10"),
            RuleDefinitionSortField.LatestVersion => definition.LatestPublishedVersion?.ToString("D10"),
            RuleDefinitionSortField.Revision => definition.Origin == DomainOrigin.BuiltIn
                ? null
                : definition.Revision.ToString("D10"),
            RuleDefinitionSortField.CreatedBy => definition.CreatedByActor?.DisplayName,
            RuleDefinitionSortField.CreatedAt => definition.CreatedAt.ToUniversalTime().ToString("O"),
            RuleDefinitionSortField.ModifiedBy => definition.UpdatedByActor?.DisplayName,
            RuleDefinitionSortField.ModifiedAt => definition.UpdatedAt.ToUniversalTime().ToString("O"),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy)),
        };

    private static string DefinitionSortName(RuleDefinition definition, string? language)
    {
        if (definition.Origin != DomainOrigin.BuiltIn || definition.Documentation is null)
            return definition.Name;

        string locale = language?.StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true
            ? "vi"
            : "en";
        return definition.Documentation.Locales.TryGetValue(locale, out RuleReferenceContent? content)
            ? content.DisplayName
            : definition.Name;
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
                    $"{content.Summary} {content.Usage} {definition.Key.Value}",
                    definition.Status.ToString());
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<RuleDefinitionSummaryDto>> HydrateSearchItemsAsync(
        IReadOnlyList<RuleCatalogSearchItem> searchItems,
        IReadOnlyList<RuleDefinition> builtInCandidates,
        Guid workspaceId,
        IRuleDefinitionRepository repository,
        CancellationToken cancellationToken)
    {
        Dictionary<string, RuleDefinition> builtInByKey = builtInCandidates
            .ToDictionary(definition => definition.Key.Value, StringComparer.Ordinal);
        IReadOnlyList<RuleDefinitionKey> workspaceKeys = searchItems
            .Where(item => item.Origin == DomainOrigin.Workspace)
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
            if (item.Origin == DomainOrigin.BuiltIn &&
                builtInByKey.TryGetValue(item.Key, out RuleDefinition? builtInDefinition))
            {
                definitions.Add(RuleContractMapper.ToSummaryDto(builtInDefinition));
            }
            else if (item.Origin == DomainOrigin.Workspace &&
                     workspaceByKey.TryGetValue(item.Key, out RuleDefinition? workspaceDefinition))
            {
                definitions.Add(RuleContractMapper.ToSummaryDto(workspaceDefinition));
            }
        }

        return definitions;
    }

    private sealed record SortableDefinition(
        RuleDefinitionSummaryDto Definition,
        string? SortValue,
        string Name);
}
