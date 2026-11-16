using System.Globalization;
using Axis.Configuration.Compilation;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Server.Tenancy;
using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>
/// <c>GET /api/studio/apps/{application}/releases/{number}</c> answers one release of the application's history in this tenant
/// as <c>{ number, previous, previousIsLive, live, changes, partChanges, modules, packages, blockers }</c>, see
/// <c>docs/reference/studio-workspace.md#release-route</c>. It checks the session (401), the author role (403) and the
/// application (404), and answers 404 for a number that is not in the history. <c>previous</c> is the number of the release
/// it compares with: the live one when this one is not live, else the one just below it, or null. <c>changes</c> and
/// <c>partChanges</c> are null without <c>previous</c>. <c>blockers</c> is the module check of
/// <see cref="ModuleUpgradeCheck"/> on the two stored releases, and is empty for the live release and when nothing is live.
/// A module's label is the one in the <c>module.json</c> that the release stores and a package's label is the one of the
/// installed package of that version, else the id stands in. It reads the tenant database and changes nothing.
/// <c>GET /api/studio/apps/{application}/release-list</c> answers one page of the application's releases as
/// <c>{ items, page, pageSize, totalCount }</c>, see <c>docs/reference/authoring.md#release-list</c>. Each release is one item, with
/// the data of its first activation and its Live and Off-standard flags. It takes the record API's <c>page</c>, <c>pageSize</c>,
/// <c>sort</c>, <c>search</c> and <c>filter</c> through <see cref="StudioListQuery{T}"/>, checks the session (401), the author
/// role (403) and the application (404), and answers 400 for a sort or a filter on a column it does not hold. It does not
/// compile the folder.
/// </summary>
internal static class StudioReleaseEndpoints
{
    private const string NoRelease = "No release has this number.";

    public static void MapStudioReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/releases/{number}", GetAsync);
        endpoints.MapGet("/api/studio/apps/{application}/release-list", ListAsync);
    }

    // A time filters by its day in UTC, as a date column does, and sorts by its instant.
    private static readonly IReadOnlyList<StudioListColumn<StudioReleaseListRow>> _listColumns =
    [
        new("number", StudioListKind.Number, row => row.Number),
        new("label", StudioListKind.Text, row => row.Label, Searchable: true),
        new("publishedAt", StudioListKind.Date, row => DateOnly.FromDateTime(row.PublishedAt.UtcDateTime), Order: row => row.PublishedAt),
        new("publishedBy", StudioListKind.Text, row => row.ActorName, Searchable: true),
        new("note", StudioListKind.Text, row => row.Note, Searchable: true),
    ];

    private static async Task<IResult> ListAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = StudioListQuery<StudioReleaseListRow>.Read(httpContext.Request, _listColumns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // A folder without a readable manifest id has never been activated, so its history is empty.
        if (StudioFolder.List(folder).FirstOrDefault(file => file.File == "application.json")?.Id is not { } applicationId)
        {
            return Results.Ok(ToResponse(query.Apply([])));
        }

        var cancellationToken = httpContext.RequestAborted;
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var active = await store.FindByApplicationIdAsync(applicationId, cancellationToken);
        var activations = await store.ListActivationsAsync(applicationId, cancellationToken);
        var items = await StudioEndpoints.ReleaseItemsAsync(store, database, activations, cancellationToken);

        // A release can be in the history more than once, so its first activation gives the row. On equal times the first
        // in the history's own order stays.
        var rows = items
            .GroupBy(item => item.ReleaseId)
            .Select(group => group.MinBy(item => item.ActivatedAt)!)
            .OrderByDescending(item => item.Number)
            .Select(item => new StudioReleaseListRow(
                item.ReleaseId, item.Number, item.Label, item.Note, item.ActivatedAt, item.Actor, item.ActorName,
                item.ReleaseId == active?.ReleaseId, item.OffStandard, item.Relaxed))
            .ToList();
        return Results.Ok(ToResponse(query.Apply(rows)));
    }

    private static StudioReleaseListResponse ToResponse(StudioListPage<StudioReleaseListRow> page) =>
        new(page.Items, page.Page, page.PageSize, page.TotalCount);

    private static async Task<IResult> GetAsync(
        string application,
        string number,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        // A folder without a readable manifest id has never been activated, so its history is empty.
        if (StudioFolder.List(folder).FirstOrDefault(file => file.File == "application.json")?.Id is not { } applicationId
            || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var requested))
        {
            return StudioEndpoints.NotFound(NoRelease);
        }

        var cancellationToken = httpContext.RequestAborted;
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var activations = await store.ListActivationsAsync(applicationId, cancellationToken);
        if (activations.FirstOrDefault(row => row.Number == requested) is not { } activation
            || await store.GetReleaseAsync(activation.ReleaseId, cancellationToken) is not { } release)
        {
            return StudioEndpoints.NotFound(NoRelease);
        }

        var active = await store.FindByApplicationIdAsync(applicationId, cancellationToken);
        var live = active?.ReleaseId == activation.ReleaseId;
        var previousIsLive = active is not null && !live && activations.Any(row => row.ReleaseId == active.ReleaseId);
        var previousActivation = previousIsLive
            ? activations.First(row => row.ReleaseId == active!.ReleaseId)
            : activations.Where(row => row.Number < requested).MaxBy(row => row.Number);
        var previous = previousActivation is null ? null : await store.GetReleaseAsync(previousActivation.ReleaseId, cancellationToken);

        var previousModules = previous is null ? null : ModulesOf(previous);
        var moduleRows = ModuleParts(ModulesOf(release), previousModules);
        var packageRows = PackageParts(packages, release, previous);
        IReadOnlyList<StudioReleaseChange>? changes = null;
        IReadOnlyList<StudioReleasePartChange>? partChanges = null;
        IReadOnlyList<StudioReleaseBlocker> blockers = [];
        if (previous is not null)
        {
            changes = StudioReleaseChanges.Compare(Contents(release), Contents(previous));
            partChanges = PartChanges(moduleRows, packageRows, previous, packages);

            // Only the live release has modules to keep, and a release replaces nothing while it is live itself.
            if (previousIsLive)
            {
                blockers =
                [
                    .. ModuleUpgradeCheck.Blockers(previous.Resources, release.Resources).Select(blocker => new StudioReleaseBlocker(
                        blocker.Module,
                        previousModules!.Find(module => module.Manifest.Id == blocker.Module) is { } module ? LabelOf(module) : blocker.Module,
                        blocker.ActiveVersion,
                        blocker.Version)),
                ];
            }
        }

        return Results.Ok(new StudioReleaseResponse(
            requested, previousActivation?.Number, previousIsLive, live, changes, partChanges, moduleRows, packageRows, blockers));
    }

    // What the given parts pin that differs from what the stored release pins, modules first and each by id.
    internal static List<StudioReleasePartChange> PartChanges(
        IReadOnlyList<StudioReleasePart> modules, IReadOnlyList<StudioReleasePart> packageParts, Release other, PackageCatalog packages) =>
        [
            .. PartChanges("module", modules, ModuleParts(ModulesOf(other), null)),
            .. PartChanges("package", packageParts, PackageParts(packages, other, null)),
        ];

    // What the release pins that differs from what the other release pins, modules and packages each by id.
    private static IEnumerable<StudioReleasePartChange> PartChanges(
        string type, IReadOnlyList<StudioReleasePart> current, IReadOnlyList<StudioReleasePart> previous) =>
        Differences(type, current, previous).OrderBy(change => change.Id, StringComparer.Ordinal);

    private static IEnumerable<StudioReleasePartChange> Differences(
        string type, IReadOnlyList<StudioReleasePart> current, IReadOnlyList<StudioReleasePart> previous)
    {
        var before = previous.ToDictionary(part => part.Id, StringComparer.Ordinal);
        foreach (var part in current)
        {
            if (!before.TryGetValue(part.Id, out var other))
            {
                yield return new StudioReleasePartChange(type, part.Id, part.Label, "added", part.Version, null);
                continue;
            }

            var order = PackageVersions.Compare(part.Version, other.Version);
            if (order != 0)
            {
                yield return new StudioReleasePartChange(type, part.Id, part.Label, order > 0 ? "upgraded" : "downgraded", part.Version, other.Version);
            }
        }

        var ids = current.Select(part => part.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var part in before.Values.Where(part => !ids.Contains(part.Id)).OrderBy(part => part.Id, StringComparer.Ordinal))
        {
            yield return new StudioReleasePartChange(type, part.Id, part.Label, "removed", null, part.Version);
        }
    }

    // The modules of a release by id, with their version in the other release when there is one.
    private static List<StudioReleasePart> ModuleParts(List<InstalledModule> modules, List<InstalledModule>? other) =>
        [
            .. modules.Select(module => new StudioReleasePart(
                module.Manifest.Id, LabelOf(module), module.Manifest.Version, other?.Find(candidate => candidate.Manifest.Id == module.Manifest.Id)?.Manifest.Version)),
        ];

    // The packages of a release by id, with their version in the other release when there is one.
    private static List<StudioReleasePart> PackageParts(PackageCatalog packages, Release release, Release? other) =>
        [
            .. release.Packages
                .OrderBy(package => package.PackageId, StringComparer.Ordinal)
                .Select(package => new StudioReleasePart(
                    package.PackageId,
                    PackageLabel(packages, package.PackageId, package.Version),
                    package.Version,
                    other?.Packages.Find(candidate => candidate.PackageId == package.PackageId)?.Version)),
        ];

    // The modules and packages that the folder's compile picks, each with its label, read even when the compile has errors.
    // A module's label is the one of the installed module of that version, and a package's is the one of its installed version.
    internal static (List<StudioReleasePart> Modules, List<StudioReleasePart> Packages) FolderParts(
        CompilationResult compiled, ModuleCatalog modules, PackageCatalog packages) =>
        (
            [
                .. compiled.ModulePins.Select(pin => new StudioReleasePart(
                    pin.Id,
                    modules.Modules.FirstOrDefault(module => module.Manifest.Id == pin.Id && module.Manifest.Version == pin.Version) is { } installed
                        ? LabelOf(installed)
                        : pin.Id,
                    pin.Version,
                    null)),
            ],
            [
                .. compiled.PackagePins
                    .OrderBy(pin => pin.Id, StringComparer.Ordinal)
                    .Select(pin => new StudioReleasePart(pin.Id, PackageLabel(packages, pin.Id, pin.Version), pin.Version, null)),
            ]);

    private static List<InstalledModule> ModulesOf(Release release) =>
        ApplicationCompiler.ReadModuleCopies(
            Contents(release).Where(resource => resource.Path.StartsWith(ApplicationLoader.ModulesFolderPrefix, StringComparison.Ordinal)), []);

    private static List<ResourceContent> Contents(Release release) =>
        [.. release.Resources.Select(resource => new ResourceContent(resource.Path, resource.Content))];

    private static string LabelOf(InstalledModule module) =>
        string.IsNullOrWhiteSpace(module.Manifest.Label) ? module.Manifest.Id : module.Manifest.Label;

    private static string PackageLabel(PackageCatalog packages, string id, string version) =>
        packages.Find(id, version)?.Manifest.Label is { Length: > 0 } label ? label : id;
}

/// <summary>One release of the history, see <see cref="StudioReleaseEndpoints"/>.</summary>
internal sealed record StudioReleaseResponse(
    int Number,
    int? Previous,
    bool PreviousIsLive,
    bool Live,
    IReadOnlyList<StudioReleaseChange>? Changes,
    IReadOnlyList<StudioReleasePartChange>? PartChanges,
    IReadOnlyList<StudioReleasePart> Modules,
    IReadOnlyList<StudioReleasePart> Packages,
    IReadOnlyList<StudioReleaseBlocker> Blockers);

/// <summary>A module or package that a release pins: its id, label and version, and its version in the release it compares with.</summary>
internal sealed record StudioReleasePart(string Id, string Label, string Version, string? PreviousVersion);

/// <summary>
/// A module or package that differs from the release it compares with. <c>Type</c> is <c>module</c> or <c>package</c> and
/// <c>Change</c> is <c>added</c>, <c>removed</c>, <c>upgraded</c> or <c>downgraded</c>.
/// </summary>
internal sealed record StudioReleasePartChange(
    string Type, string Id, string Label, string Change, string? Version, string? PreviousVersion);

/// <summary>A module of the live release that the release cannot replace. <c>Version</c> is null when the release lacks the module.</summary>
internal sealed record StudioReleaseBlocker(string Module, string Label, string LiveVersion, string? Version);

/// <summary>
/// One release of the release list: the data of its first activation, with <c>Live</c> set on the release that runs and the
/// release's own <c>OffStandard</c> and <c>Relaxed</c>, see <see cref="StudioReleaseEndpoints"/>.
/// </summary>
internal sealed record StudioReleaseListRow(
    Guid ReleaseId,
    int Number,
    string? Label,
    string? Note,
    DateTimeOffset PublishedAt,
    string Actor,
    string? ActorName,
    bool Live,
    bool OffStandard,
    IReadOnlyList<StudioRelaxedPoint> Relaxed);

/// <summary>One page of the release list with the number of releases that matched, see <see cref="StudioReleaseEndpoints"/>.</summary>
internal sealed record StudioReleaseListResponse(IReadOnlyList<StudioReleaseListRow> Items, int Page, int PageSize, long TotalCount);
