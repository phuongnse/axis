namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>site</c> resource: an entry point of the application with its path, title, locales and
/// navigation. Whether its pages, locales and text keys exist is checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record SiteResource : Resource
{
    public required string Path { get; init; }

    public required TextReference Title { get; init; }

    public required SiteLocales Locales { get; init; }

    public required IReadOnlyList<NavigationEntry> Navigation { get; init; }

    /// <summary>
    /// The names of the identity providers that people may sign in with. Null allows every identity
    /// provider of the application.
    /// </summary>
    public IReadOnlyList<string>? SignIn { get; init; }

    /// <summary>
    /// The page that opens each entity's records, by entity name and page name. The compiler checks that each
    /// names a loaded entity and a page with a form or detail over it. Null names none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? RecordPages { get; init; }
}
