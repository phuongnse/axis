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
}
