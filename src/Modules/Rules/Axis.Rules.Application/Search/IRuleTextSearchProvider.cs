namespace Axis.Rules.Application.Search;

public sealed record RuleTextSearchDocument(
    string Key,
    string Title,
    string Content);

public sealed record RuleTextSearchMatch(
    string Key,
    double Relevance);

public interface IRuleTextSearchProvider
{
    Task<IReadOnlyList<RuleTextSearchMatch>> SearchAsync(
        IReadOnlyList<RuleTextSearchDocument> documents,
        string query,
        CancellationToken cancellationToken = default);
}
