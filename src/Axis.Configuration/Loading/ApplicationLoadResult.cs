using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>
/// The outcome of loading an application folder. Typed resources are present for every file that
/// passed schema validation, even when other files failed. <see cref="Resources"/> holds the
/// canonical content of those same files, in path order, except scenario files: scenarios stay out
/// of the release and its content hash.
/// </summary>
/// <remarks>
/// When <see cref="HasErrors"/> is true, <see cref="Application"/>, <see cref="Entities"/>,
/// <see cref="Sites"/>, <see cref="Pages"/>, <see cref="Texts"/>, <see cref="Seeds"/>,
/// <see cref="DataSources"/>, <see cref="Rules"/>, <see cref="Sequences"/>, <see cref="Processes"/>, <see cref="Forms"/>, <see cref="Roles"/>, <see cref="Policies"/>, <see cref="Connectors"/>, <see cref="Events"/>, <see cref="Endpoints"/>, <see cref="Triggers"/>, <see cref="Migrations"/> and <see cref="Scenarios"/> hold only
/// the files that passed schema validation. They may be used to find further diagnostics, but never
/// to build a model or a release. When the folder could not be listed, the result has no
/// application, entities, sites, pages, texts, seeds, data sources, rules, sequences, processes, forms, roles, policies, connectors, events, endpoints, triggers, migrations, scenarios or resources and a single <see cref="DiagnosticCodes.UnlistableFolder"/>
/// diagnostic.
/// </remarks>
/// <param name="UnloadedEntityNames">
/// The <c>name</c> of every <c>entity</c> file that was not loaded because of its own errors, such
/// as a schema violation, compared ignoring letter case. A reference to one of these names is not
/// reported again; the file's own diagnostics already are.
/// </param>
/// <param name="UnloadedPageNames">
/// The <c>name</c> of every <c>page</c> file that was not loaded because of its own errors,
/// compared ignoring letter case. A reference to one of these names is not reported again either.
/// </param>
/// <param name="UnloadedDataSourceNames">
/// The <c>name</c> of every <c>dataSource</c> file that was not loaded because of its own errors,
/// compared ignoring letter case. A reference to one of these names is not reported again either.
/// </param>
/// <param name="UnloadedSequenceNames">
/// The <c>name</c> of every <c>sequence</c> file that was not loaded because of its own errors,
/// compared ignoring letter case. A reference to one of these names is not reported again either.
/// </param>
/// <param name="UnloadedFormNames">
/// The <c>name</c> of every <c>form</c> file that was not loaded because of its own errors,
/// compared ignoring letter case. A reference to one of these names is not reported again either.
/// </param>
/// <param name="UnloadedProcessNames">
/// The <c>name</c> of every <c>process</c> file that was not loaded because of its own errors,
/// compared ignoring letter case. A reference to one of these names is not reported again either.
/// </param>
/// <param name="UnloadedRoleNames">
/// The <c>name</c> of every <c>role</c> file that was not loaded because of its own errors,
/// compared exactly, as role names are. A reference to one of these names is not reported again either.
/// </param>
public sealed record ApplicationLoadResult(
    ApplicationManifest? Application,
    IReadOnlyList<EntityResource> Entities,
    IReadOnlyList<SiteResource> Sites,
    IReadOnlyList<PageResource> Pages,
    IReadOnlyList<TextResource> Texts,
    IReadOnlyList<SeedResource> Seeds,
    IReadOnlyList<DataSourceResource> DataSources,
    IReadOnlyList<RuleResource> Rules,
    IReadOnlyList<SequenceResource> Sequences,
    IReadOnlyList<ProcessResource> Processes,
    IReadOnlyList<FormResource> Forms,
    IReadOnlyList<RoleResource> Roles,
    IReadOnlyList<PolicyResource> Policies,
    IReadOnlyList<ResourceContent> Resources,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlySet<string> UnloadedEntityNames,
    IReadOnlySet<string> UnloadedPageNames,
    IReadOnlySet<string> UnloadedDataSourceNames,
    IReadOnlySet<string> UnloadedSequenceNames,
    IReadOnlySet<string> UnloadedFormNames,
    IReadOnlySet<string> UnloadedProcessNames,
    IReadOnlySet<string> UnloadedRoleNames)
{
    /// <summary>The <c>connector</c> resources, in path order.</summary>
    public IReadOnlyList<ConnectorResource> Connectors { get; init; } = [];

    /// <summary>
    /// The <c>name</c> of every <c>connector</c> file that was not loaded because of its own errors,
    /// compared ignoring letter case. A reference to one of these names is not reported again either.
    /// </summary>
    public IReadOnlySet<string> UnloadedConnectorNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>event</c> resources, in path order.</summary>
    public IReadOnlyList<EventResource> Events { get; init; } = [];

    /// <summary>
    /// The <c>name</c> of every <c>event</c> file that was not loaded because of its own errors,
    /// compared ignoring letter case. A reference to one of these names is not reported again either.
    /// </summary>
    public IReadOnlySet<string> UnloadedEventNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>endpoint</c> resources, in path order.</summary>
    public IReadOnlyList<EndpointResource> Endpoints { get; init; } = [];

    /// <summary>
    /// The <c>name</c> of every <c>endpoint</c> file that was not loaded because of its own errors,
    /// compared ignoring letter case. A reference to one of these names is not reported again either.
    /// </summary>
    public IReadOnlySet<string> UnloadedEndpointNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>trigger</c> resources, in path order.</summary>
    public IReadOnlyList<TriggerResource> Triggers { get; init; } = [];

    /// <summary>The <c>migration</c> resources, in path order.</summary>
    public IReadOnlyList<MigrationResource> Migrations { get; init; } = [];

    /// <summary>The <c>scenario</c> resources, in path order. <see cref="Resources"/> does not hold them.</summary>
    public IReadOnlyList<ScenarioResource> Scenarios { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
