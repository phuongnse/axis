using System.Text;
using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Data.Profiles;
using Axis.Server.Tenancy;
using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>
/// <c>GET /api/studio/apps/{application}/resource-activity</c> answers <c>{ items }</c>, one for each resource of the
/// folder and of the modules the folder pins, see <c>docs/reference/studio-resource-form.md#resource-activity</c>. It
/// checks the session (401), the author role (403) and the application (404). It compiles the folder to find the pinned
/// modules, reads the live release and the write records of <see cref="StudioWriteRecords"/>, and changes nothing.
/// </summary>
internal static class StudioActivityEndpoints
{
    private const string New = "new";
    private const string Changed = "changed";
    private const string Published = "published";

    public static void MapStudioActivityEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/studio/apps/{application}/resource-activity", GetAsync);

    // An item before its writer is known, with the file and the version whose record would name the writer.
    private sealed record Pending(
        string Id,
        string? Module,
        string? Status,
        IReadOnlyDictionary<string, string>? Titles,
        IReadOnlyDictionary<string, string>? Descriptions,
        string? WriterFile,
        string? WriterVersion);

    private static async Task<IResult> GetAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var cancellationToken = httpContext.RequestAborted;
        var connection = await database.GetConnectionAsync(cancellationToken);
        var released = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, StudioWriteRecords.Write> writes = new Dictionary<string, StudioWriteRecords.Write>();
        var hasRelease = false;
        if (StudioFolder.ReadManifest(folder)?.Id is { } applicationId)
        {
            var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
            if (await store.FindByApplicationIdAsync(applicationId, cancellationToken) is { } active
                && await store.GetReleaseAsync(active.ReleaseId, cancellationToken) is { } live)
            {
                hasRelease = true;
                foreach (var resource in live.Resources)
                {
                    released[resource.Path] = resource.Content;
                }
            }

            writes = await StudioWriteRecords.ReadAsync(connection, applicationId, cancellationToken);
        }

        var (resources, files) = StudioFolder.ListWithContent(folder);
        var releasedOwn = released
            .Where(pair => !pair.Key.StartsWith(ApplicationLoader.ModulesFolderPrefix, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var pending = FolderItems(resources, files, hasRelease, releasedOwn);
        pending.AddRange(ModuleItems(folder, packages, modules, resources, hasRelease, released, releasedOwn));

        // The record names a writer only while the file still has the version that the write left.
        StudioWriteRecords.Write? WriterOf(Pending item) =>
            item.WriterFile is not null && writes.TryGetValue(item.WriterFile, out var write) && write.Version == item.WriterVersion
                ? write
                : null;

        var names = await UserProfiles.FindDisplayNamesAsync(
            connection, [.. pending.Select(WriterOf).OfType<StudioWriteRecords.Write>().Select(write => write.UserId)], cancellationToken);
        return Results.Ok(new StudioResourceActivityResponse(
            [
                .. pending.Select(item =>
                {
                    var write = WriterOf(item);
                    return new StudioResourceActivityItem(
                        item.Id,
                        item.Module,
                        item.Status,
                        item.Titles,
                        item.Descriptions,
                        write is null ? null : names.GetValueOrDefault(write.UserId) ?? write.UserName,
                        write?.WrittenAt);
                }),
            ]));
    }

    // The resources of the folder by file, then a released file that no longer loads, which is changed and keeps its id.
    private static List<Pending> FolderItems(
        IReadOnlyList<StudioFile> resources,
        IReadOnlyList<ResourceContent> files,
        bool hasRelease,
        Dictionary<string, string> releasedOwn)
    {
        var contents = files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        var items = new List<(string File, Pending Item)>();
        foreach (var resource in resources)
        {
            var status = resource.Kind == ResourceKinds.Scenario
                ? null
                : StatusOf(hasRelease, contents[resource.File], releasedOwn.GetValueOrDefault(resource.File));
            items.Add((resource.File, new Pending(resource.Id.ToString("D"), null, status, null, null, resource.File, resource.Version)));
        }

        var listed = resources.Select(resource => resource.File).ToHashSet(StringComparer.Ordinal);
        var ids = resources.Select(resource => resource.Id).ToHashSet();
        foreach (var (path, content) in releasedOwn)
        {
            if (!contents.ContainsKey(path) || listed.Contains(path) || ReleasedId(content) is not { } id || !ids.Add(id))
            {
                continue;
            }

            items.Add((path, new Pending(id.ToString("D"), null, Changed, null, null, null, null)));
        }

        items.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        return [.. items.Select(item => item.Item)];
    }

    // The id of a released file, or null for a scenario or a file whose id cannot be read.
    private static Guid? ReleasedId(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return StudioReleaseChanges.StringProperty(root, "kind") == ResourceKinds.Scenario
                ? null
                : Guid.TryParse(StudioReleaseChanges.StringProperty(root, "id"), out var id) ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The resources of each module version the folder pins, read from the module's own files and texts.
    private static List<Pending> ModuleItems(
        string folder,
        PackageCatalog packages,
        ModuleCatalog modules,
        IReadOnlyList<StudioFile> folderResources,
        bool hasRelease,
        Dictionary<string, string> released,
        Dictionary<string, string> releasedOwn)
    {
        var items = new List<Pending>();
        var pins = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules).ModulePins;
        IReadOnlyList<StudioFile>? releasedResources = null;
        foreach (var pin in pins.OrderBy(pin => pin.Id, StringComparer.Ordinal))
        {
            var installed = modules.Modules.FirstOrDefault(item => item.Manifest.Id == pin.Id && item.Manifest.Version == pin.Version);
            if (installed is null)
            {
                continue;
            }

            var contents = installed.Files
                .Where(file => file.Path != ModuleCatalog.ManifestFileName)
                .ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
            var moduleResources = StudioFolder.Resources(contents.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value))));
            var titles = StudioResourceTitles.Read(moduleResources);
            foreach (var resource in moduleResources)
            {
                string? status = null;
                var layer = resource.Name is { } name ? StudioWrites.FindLayer(folderResources, pin.Id, name) : null;
                if (resource.Kind != ResourceKinds.Scenario)
                {
                    var copy = released.GetValueOrDefault($"{ApplicationLoader.ModulesFolderPrefix}{pin.Id}/{resource.File}");
                    status = StatusOf(hasRelease, contents[resource.File], copy);
                    if (status == Published && resource.Name is { } layerName)
                    {
                        releasedResources ??= StudioFolder.Resources(releasedOwn.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value))));
                        status = LayerDiffers(layer?.File, StudioWrites.FindLayer(releasedResources, pin.Id, layerName)?.File) ? Changed : Published;
                    }
                }

                var (resourceTitles, descriptions, _) = titles[resource.Id];
                items.Add(new Pending(
                    resource.Id.ToString("D"), pin.Id, status, resourceTitles, descriptions, layer?.File.File, layer?.File.Version));
            }
        }

        return items;
    }

    // A layer that only one side has, or whose canonical JSON differs, is a difference.
    private static bool LayerDiffers(StudioFile? current, StudioFile? release) =>
        (current, release) switch
        {
            (null, null) => false,
            (null, _) or (_, null) => true,
            _ => !string.Equals(
                StudioReleaseChanges.Canonical(Encoding.UTF8.GetString(current.Bytes)),
                StudioReleaseChanges.Canonical(Encoding.UTF8.GetString(release.Bytes)),
                StringComparison.Ordinal),
        };

    // New without a release or a released copy, changed when the canonical JSON differs, else published.
    private static string StatusOf(bool hasRelease, string content, string? releasedContent) =>
        !hasRelease || releasedContent is null
            ? New
            : string.Equals(StudioReleaseChanges.Canonical(content), StudioReleaseChanges.Canonical(releasedContent), StringComparison.Ordinal)
                ? Published
                : Changed;
}

/// <summary>The resource activity of an application, see <see cref="StudioActivityEndpoints"/>.</summary>
internal sealed record StudioResourceActivityResponse(IReadOnlyList<StudioResourceActivityItem> Items);

/// <summary>
/// One resource of the folder, or of a module when <c>Module</c> is set. <c>Status</c> is <c>new</c>, <c>changed</c> or
/// <c>published</c>, and null for a scenario. <c>Titles</c> and <c>Descriptions</c> are answered for a module resource only.
/// <c>ModifiedBy</c> and <c>ModifiedAt</c> are null unless the file still has the version that the recorded write left.
/// </summary>
internal sealed record StudioResourceActivityItem(
    string Id,
    string? Module,
    string? Status,
    IReadOnlyDictionary<string, string>? Titles,
    IReadOnlyDictionary<string, string>? Descriptions,
    string? ModifiedBy,
    DateTimeOffset? ModifiedAt);
