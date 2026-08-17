using Axis.Rules.Domain;
using Axis.Shared.Application;

namespace Axis.Rules.Application.Search;

public sealed record RuleCatalogSearchItem(
    RuleOrigin Origin,
    string Key);

public sealed record RuleCatalogSearchPage(
    IReadOnlyList<RuleCatalogSearchItem> Items,
    int TotalCount);

public interface IRuleCatalogSearchProvider
{
    Task<RuleCatalogSearchPage> SearchAsync(
        Guid workspaceId,
        IReadOnlyList<RuleTextSearchDocument> systemDocuments,
        bool includeWorkspace,
        RuleLifecycleStatus? workspaceStatus,
        RuleDefinitionSortField? sortBy,
        CollectionSortDirection? sortDirection,
        int skip,
        int take,
        string query,
        CancellationToken cancellationToken = default);
}
