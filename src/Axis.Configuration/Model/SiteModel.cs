using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled site with its navigation resolved to pages.</summary>
public sealed record SiteModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    public required TextReference Title { get; init; }

    public required SiteLocales Locales { get; init; }

    /// <summary>The site's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required IReadOnlyList<NavigationModel> Navigation { get; init; }
}
