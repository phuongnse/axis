namespace Axis.Server.Http;

/// <summary>
/// The <c>search</c> query parameter of a list, as the record API defines it. It is given at most
/// once, has at most <see cref="MaxLength"/> characters and no U+0000, and an empty value means no
/// search. What a search matches is the route's own rule.
/// </summary>
internal static class SearchQuery
{
    public const int MaxLength = 200;

    /// <summary>Whether the request gives <c>search</c> more than once. A repeated one binds as its values joined by commas.</summary>
    public static bool IsRepeated(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Query["search"].Count > 1;
    }

    /// <summary>The error message of a search that is repeated.</summary>
    public static string[] RepeatedError => ["Must be given at most once."];

    /// <summary>The error message of <paramref name="search"/>, or null when its length and characters are valid.</summary>
    public static string[]? InvalidError(string search)
    {
        ArgumentNullException.ThrowIfNull(search);

        return search.Length > MaxLength || search.Contains('\0', StringComparison.Ordinal)
            ? [$"Must be at most {MaxLength} characters with no U+0000."]
            : null;
    }
}
