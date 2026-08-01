using Axis.Rules.Domain;

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
        int skip,
        int take,
        string query,
        CancellationToken cancellationToken = default);
}
