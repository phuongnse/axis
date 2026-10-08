using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled application: its manifest, every entity, site, page, seed and data source with
/// references resolved, and its texts.
/// </summary>
public sealed record ApplicationModel
{
    public required ApplicationManifest Manifest { get; init; }

    public required IReadOnlyList<EntityModel> Entities { get; init; }

    /// <summary>The application's sites, in path order.</summary>
    public IReadOnlyList<SiteModel> Sites { get; init; } = [];

    /// <summary>The application's pages, in path order.</summary>
    public IReadOnlyList<PageModel> Pages { get; init; } = [];

    /// <summary>The application's text resources, one per locale, in path order.</summary>
    public IReadOnlyList<TextResource> Texts { get; init; } = [];

    /// <summary>The application's seeds, in path order.</summary>
    public IReadOnlyList<SeedModel> Seeds { get; init; } = [];

    /// <summary>The application's data sources, in path order.</summary>
    public IReadOnlyList<DataSourceModel> DataSources { get; init; } = [];

    /// <summary>Finds an entity by name, ignoring letter case.</summary>
    public bool TryGetEntity(string name, [NotNullWhen(true)] out EntityModel? entity)
    {
        entity = Entities.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return entity is not null;
    }

    /// <summary>Finds a data source by name, ignoring letter case.</summary>
    public bool TryGetDataSource(string name, [NotNullWhen(true)] out DataSourceModel? dataSource)
    {
        dataSource = DataSources.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return dataSource is not null;
    }
}
