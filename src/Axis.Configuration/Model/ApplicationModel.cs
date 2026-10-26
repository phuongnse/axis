using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled application: its manifest, every entity, site, page, seed, data source, process,
/// form, policy, event, endpoint and trigger with references resolved, its migrations, its connectors, its roles,
/// its texts and the extension package and module versions it pins.
/// </summary>
public sealed record ApplicationModel
{
    public required ApplicationManifest Manifest { get; init; }

    public required IReadOnlyList<EntityModel> Entities { get; init; }

    /// <summary>The application's time zone, UTC when the manifest names none. It is part of the release.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

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

    /// <summary>The application's forms, in path order.</summary>
    public IReadOnlyList<FormModel> Forms { get; init; } = [];

    /// <summary>The application's roles, in path order.</summary>
    public IReadOnlyList<RoleResource> Roles { get; init; } = [];

    /// <summary>The application's policies, in path order.</summary>
    public IReadOnlyList<PolicyModel> Policies { get; init; } = [];

    /// <summary>The application's connectors, in path order.</summary>
    public IReadOnlyList<ConnectorModel> Connectors { get; init; } = [];

    /// <summary>The application's events, in path order.</summary>
    public IReadOnlyList<EventModel> Events { get; init; } = [];

    /// <summary>The application's endpoints, in path order.</summary>
    public IReadOnlyList<EndpointModel> Endpoints { get; init; } = [];

    /// <summary>The application's triggers, in path order.</summary>
    public IReadOnlyList<TriggerModel> Triggers { get; init; } = [];

    /// <summary>The application's migrations, in ordinal order of their file path.</summary>
    public IReadOnlyList<MigrationModel> Migrations { get; init; } = [];

    /// <summary>The extension package versions the application pins, one per <c>packages</c> entry, in the order written.</summary>
    public IReadOnlyList<PinnedPackageModel> Packages { get; init; } = [];

    /// <summary>The module versions the application pins, one per <c>modules</c> entry, in the order written.</summary>
    public IReadOnlyList<PinnedModuleModel> Modules { get; init; } = [];

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

    /// <summary>Finds an endpoint by name, ignoring letter case.</summary>
    public bool TryGetEndpoint(string name, [NotNullWhen(true)] out EndpointModel? endpoint)
    {
        endpoint = Endpoints.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return endpoint is not null;
    }

    /// <summary>Finds a form by name, ignoring letter case.</summary>
    public bool TryGetForm(string name, [NotNullWhen(true)] out FormModel? form)
    {
        form = Forms.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return form is not null;
    }
}
