using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled application: its manifest, every entity, site, page, seed, data source and process
/// with references resolved, and its texts.
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

    /// <summary>The application's processes, in path order.</summary>
    public IReadOnlyList<ProcessModel> Processes { get; init; } = [];

    /// <summary>Finds an entity by name, ignoring letter case.</summary>
    public bool TryGetEntity(string name, [NotNullWhen(true)] out EntityModel? entity)
    {
        entity = Entities.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return entity is not null;
    }

    /// <summary>Finds an entity by id; <see langword="null"/> when the application has none.</summary>
    public EntityModel? FindEntity(Guid id) => Entities.FirstOrDefault(candidate => candidate.Id == id);

    /// <summary>Whether a child-collection field of any entity names this entity, so it has no record routes of its own.</summary>
    public bool IsChildEntity(EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Entities.Any(owner => owner.Fields.Any(field => field.Type == FieldType.ChildCollection && field.Target?.Id == entity.Id));
    }

    /// <summary>Finds a data source by name, ignoring letter case.</summary>
    public bool TryGetDataSource(string name, [NotNullWhen(true)] out DataSourceModel? dataSource)
    {
        dataSource = DataSources.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return dataSource is not null;
    }

    /// <summary>Finds a process by name, ignoring letter case.</summary>
    public bool TryGetProcess(string name, [NotNullWhen(true)] out ProcessModel? process)
    {
        process = Processes.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return process is not null;
    }
}
