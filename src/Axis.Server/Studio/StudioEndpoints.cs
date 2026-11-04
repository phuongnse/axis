using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Core.Tenancy;
using Axis.Data.Profiles;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Json.Patch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// The authoring API, for an author who edits the application folders in
/// <c>Studio:Folders</c>. <c>GET /api/studio/apps</c> lists the application names of those folders, as
/// <c>{ items: [{ name }] }</c> in folder order. A folder without a readable manifest name is left out,
/// and a name repeated ignoring letter case appears once. It checks the session (401) and the author role
/// (403) like the routes below. <c>GET /api/studio/apps/{application}/resources</c> lists every resource of
/// the folder, sorted by file, each with its <c>id</c>, <c>kind</c>, <c>name</c>, <c>file</c>, <c>version</c> and
/// <c>usedBy</c> count. <c>GET /api/studio/apps/{application}/resources/{id}</c> returns one resource with its
/// JSON <c>content</c>, and its <c>version</c> as a quoted <c>ETag</c>. The version is the SHA-256 of the
/// file's bytes. A request is checked in order: session (401), author role with Studio on (403),
/// application (404), then resource (404). The problem titles never contain text from the request.
/// Only real <c>*.json</c> files inside a listed folder are reachable.
/// <c>GET /api/studio/apps/{application}/diagnostics</c> compiles the folder in memory against the installed
/// extension packages and modules and answers every compile diagnostic. It touches no database and activates nothing.
/// <c>GET /api/studio/apps/{application}/names</c> answers every name the application defines, module names
/// included, from the same compile. It touches no database and answers even when the folder has errors.
/// <c>GET /api/studio/apps/{application}/policy-matrix</c> answers what each role may do on each root entity, data
/// source, process and endpoint, from the same compile, and answers even when the folder has errors.
/// <c>GET /api/studio/apps/{application}/modules/{module}/resources/{id}</c> answers one file of the module version the
/// application pins, the highest installed version in its range, in the shape of a resource read, with <c>file</c> as
/// <c>modules/&lt;module&gt;/&lt;path&gt;</c> and no <c>ETag</c>. Its <c>content</c> has the application's layer applied, and it adds
/// <c>layer</c> (<c>{ id, file, version }</c> or null), <c>marks</c> and <c>removed</c>, see <see cref="StudioModuleLayers"/>. It answers 404 for an unknown application, a module the
/// application does not pin and an unknown resource, and never answers <c>module.json</c>. The page metadata route of
/// <see cref="StudioPageMetadataEndpoints"/> finds a module page with the same lookup.
/// <c>PUT /api/studio/apps/{application}/modules/{module}/resources/{id}</c> takes the edited module resource whole and
/// writes the application's customization layer for it with only what differs from the pinned module version, or deletes
/// the layer when nothing differs. It answers <c>{ file, version, diagnostics }</c>, with <c>file</c> and <c>version</c> null
/// when no layer is left. After the lookup above it answers 428 without <c>If-Match</c> or <c>If-None-Match</c>, 400 for both,
/// 415 for another content type, 400 for a body that is not an object, a module scenario, an ambiguous name, an edit that
/// cannot be a layer and a contract edit beyond added states and transitions, 409 for a stale version and 503 when the
/// write lock cannot be taken.
/// <c>GET /api/studio/apps/{application}/resources/{id}/references</c> answers <c>{ uses, usedBy }</c> from the same
/// compile's reference index, and <c>GET /api/studio/apps/{application}/texts/{key}/references</c> answers
/// <c>{ usedBy }</c>. A text key that nothing defines or uses answers 404.
/// <c>PATCH /api/studio/apps/{application}/resources/{id}</c> applies an RFC 6902 JSON Patch to one file
/// when <c>If-Match</c> holds the file's current version, and answers <c>{ version, diagnostics }</c>.
/// After the checks above it answers 428 without <c>If-Match</c>, 415 for another content type, 400 for a
/// bad patch or a patch that changes <c>id</c> or <c>kind</c>, and 409 for a stale version. A schema or
/// compile error never blocks the save.
/// <c>POST /api/studio/apps/{application}/resources</c> writes a new resource from its JSON body into its
/// kind's folder and answers 201 with <c>{ id, kind, name, file, version, diagnostics }</c>. After the checks
/// above it answers 415 for a content type other than <c>application/json</c>, 400 for a body that is not
/// a resource or whose <c>id</c>, <c>kind</c> or <c>name</c> cannot form a safe file name, and 409 when the
/// <c>id</c> or the file exists. <c>DELETE /api/studio/apps/{application}/resources/{id}</c> removes one
/// file when <c>If-Match</c> holds its current version and answers <c>{ diagnostics }</c>. It answers 428
/// without <c>If-Match</c>, 409 for a stale version and 400 for the manifest.
/// <c>PUT /api/studio/apps/{application}/texts/{key}</c> sets one text key in every locale's text resource from
/// <c>{ texts: { &lt;locale&gt;: &lt;text&gt; } }</c> and answers <c>{ resources: [{ id, locale, version }], diagnostics }</c>.
/// After the checks above it answers 415 for a content type other than <c>application/json</c>, 400 for a body that
/// does not name exactly the folder's locales with string texts, 409 for a text resource whose <c>texts</c> is not an
/// object and 503 when the write lock cannot be taken. It takes no <c>If-Match</c>.
/// <c>POST /api/studio/apps/{application}/changes</c> applies 1 to 20 creates and patches in one call under one write
/// lock, checks every change before it writes any, and answers <c>{ items: [{ id, version }], diagnostics }</c>.
/// The routes that validate and publish are in <see cref="StudioPublishEndpoints"/>, and the expression check is in
/// <see cref="StudioExpressionEndpoints"/>, and the template list is in <see cref="StudioTemplateEndpoints"/>.
/// In a cell, the create, patch, <c>changes</c>, module resource save and text write routes check the tenant's storage
/// quota (403 <c>quota-exceeded</c>) right after the session, and a delete never does, see
/// <c>docs/reference/tenant-quotas.md#refusals</c>.
/// <c>GET /api/studio/apps/{application}/releases</c> answers the application's release history in this tenant,
/// newest first, as <c>{ active, folderContentHash, items }</c>. <c>active</c> is the active release or null, and
/// <c>folderContentHash</c> is the content hash the folder compiles to now, or null when it has errors. Each item has
/// <c>offStandard</c> and <c>relaxed: [{ module, point, reason }]</c>, read from the <c>application.json</c> stored with
/// that release. It reads the tenant database and changes nothing.
/// </summary>
internal static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps", AppsAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resource-activity", ResourceActivityAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources/{id}", GetAsync);
        endpoints.MapGet("/api/studio/apps/{application}/modules/{module}/resources/{id}", ModuleResourceAsync);
        endpoints.MapPut("/api/studio/apps/{application}/modules/{module}/resources/{id}", ModuleResourceSaveAsync).WithStorageQuota();
        endpoints.MapPost("/api/studio/apps/{application}/resources", CreateAsync).WithStorageQuota();
        endpoints.MapPost("/api/studio/apps/{application}/changes", ChangesAsync).WithStorageQuota();
        endpoints.MapGet("/api/studio/apps/{application}/diagnostics", DiagnosticsAsync);
        endpoints.MapGet("/api/studio/apps/{application}/names", NamesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/policy-matrix", PolicyMatrixAsync);
        endpoints.MapGet("/api/studio/apps/{application}/releases", ReleasesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/releases/{number:int}", ReleaseAsync);
        endpoints.MapGet("/api/studio/apps/{application}/relaxed", RelaxedAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources/{id}/references", ResourceReferencesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/texts/{key}/references", TextReferencesAsync);
        endpoints.MapPatch("/api/studio/apps/{application}/resources/{id}", PatchAsync).WithStorageQuota();
        endpoints.MapDelete("/api/studio/apps/{application}/resources/{id}", DeleteAsync);
        endpoints.MapPut("/api/studio/apps/{application}/texts/{key}", TextWriteAsync).WithStorageQuota();
        endpoints.MapStudioPublishEndpoints();
        endpoints.MapStudioExpressionEndpoints();
        endpoints.MapStudioPageMetadataEndpoints();
        endpoints.MapStudioTemplateEndpoints();
        endpoints.MapStudioApplicationEndpoints();
        endpoints.MapStudioCatalogEndpoints();
    }

    private static async Task<IResult> AppsAsync(HttpContext httpContext, StudioOptions options)
    {
        if (await RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        var studio = ForRequest(httpContext, options);
        return Results.Ok(new StudioApplicationListResponse(
            [.. StudioFolder.Applications(studio).Select(name => DescribeApplication(studio, name))]));
    }

    /// <summary>
    /// An application of the list with its title and description per locale, as <see cref="StudioApplicationTexts"/> reads
    /// them. A folder that gives neither leaves the name, which Studio then shows.
    /// </summary>
    private static StudioApplicationItem DescribeApplication(StudioOptions options, string name)
    {
        if (StudioFolder.Find(options, name) is not { } folder)
        {
            return new StudioApplicationItem(name);
        }

        var (titles, descriptions) = StudioApplicationTexts.Read(folder);
        return new StudioApplicationItem(name, titles, descriptions);
    }

    private static async Task<IResult> ListAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var usedBy = StudioReferences.Compile(folder, packages, modules).Edges
            .Select(edge => edge.To)
            .OfType<ResourceNode>()
            .GroupBy(node => node.Id)
            .ToDictionary(group => group.Key, group => group.Count());
        var files = StudioFolder.List(folder);
        var texts = TextsByLocale(files);
        var items = files
            .Select(file => new StudioResourceItem(
                file.Id.ToString("D"),
                file.Kind,
                file.Name,
                file.File,
                file.Version,
                usedBy.GetValueOrDefault(file.Id),
                ResourceTitles(file, texts),
                file.Kind == "text" ? TextLocale(file) : null,
                ResourceDescriptions(file, texts)))
            .ToList();
        return Results.Ok(new StudioResourceListResponse(items));
    }

    /// <summary>The texts of each text resource of the folder, by its locale. A file that does not parse is left out.</summary>
    private static Dictionary<string, Dictionary<string, string>> TextsByLocale(IReadOnlyList<StudioFile> files)
    {
        var byLocale = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in files.Where(file => file.Kind == "text"))
        {
            try
            {
                if (JsonNode.Parse(file.Bytes) is JsonObject root
                    && root["locale"]?.GetValue<string>() is { } locale
                    && root["texts"] is JsonObject entries)
                {
                    var target = byLocale.TryGetValue(locale, out var existing)
                        ? existing
                        : byLocale[locale] = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (key, value) in entries)
                    {
                        if (value is JsonValue text && text.TryGetValue<string>(out var line))
                        {
                            target[key] = line;
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return byLocale;
    }

    /// <summary>Proposed: the text of a resource's <c>description</c> in each locale that holds its key, or null when it has none.</summary>
    private static Dictionary<string, string>? ResourceDescriptions(
        StudioFile file, Dictionary<string, Dictionary<string, string>> texts)
    {
        string? key;
        try
        {
            key = (JsonNode.Parse(file.Bytes) as JsonObject)?["description"]?["textKey"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }

        if (key is null)
        {
            return null;
        }

        var descriptions = texts
            .Where(locale => locale.Value.ContainsKey(key))
            .ToDictionary(locale => locale.Key, locale => locale.Value[key], StringComparer.Ordinal);
        return descriptions.Count > 0 ? descriptions : null;
    }

    /// <summary>
    /// Proposed: what the Studio lists show beside each resource and the files alone cannot say: whether it is live. It reads
    /// the tenant database, so it is a route of its own and the resource list keeps working without it.
    /// </summary>
    private static async Task<IResult> ResourceActivityAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        TenantDatabase database)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var cancellationToken = httpContext.RequestAborted;
        var files = StudioFolder.List(folder);
        if (files.FirstOrDefault(file => file.File == "application.json")?.Id is not { } applicationId)
        {
            return Results.Ok(new StudioResourceActivityResponse(
                [.. files.Select(file => new StudioResourceActivityItem(file.Id.ToString("D"), "new"))]));
        }

        var context = await database.GetConfigurationAsync(cancellationToken);
        var active = await new ActiveReleaseStore(context).FindByApplicationIdAsync(applicationId, cancellationToken);
        // A file the release does not hold is new, whether or not it loads. A file it holds that no longer loads counts as
        // removed from the comparison, so it shows as changed rather than as what the release holds.
        var live = active is null
            ? []
            : (await context.ReleaseResources
                .Where(resource => resource.ReleaseId == active.ReleaseId)
                .Select(resource => resource.Path)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        var compiled = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules);
        var changed = active is null
            ? []
            : (await LiveChangesAsync(database, folder, compiled, active.ReleaseId, cancellationToken))
                .Select(change => change.Path)
                .ToHashSet(StringComparer.Ordinal);
        string Status(string path) => !live.Contains(path) ? "new" : changed.Contains(path) ? "changed" : "published";
        return Results.Ok(new StudioResourceActivityResponse(
            [.. files.Select(file => new StudioResourceActivityItem(file.Id.ToString("D"), Status(file.File))),
            .. ModuleActivity(compiled, Status)]));
    }

    /// <summary>
    /// Proposed: the resources a module brings, each with the id in its file, which is the id the names answer, its file in
    /// a release, and its label and description in each locale, as the resource list answers them for the folder's own. A
    /// module has a status like the application's own: declaring it, a new version or a layer changes it. A folder with
    /// errors has no compiled files, so it lists none.
    /// </summary>
    private static List<StudioResourceActivityItem> ModuleActivity(CompilationResult compiled, Func<string, string> status)
    {
        var files = compiled.Resources
            .Where(resource => resource.Path.StartsWith("modules/", StringComparison.Ordinal))
            .Select(resource => (resource.Path, Root: JsonNode.Parse(resource.Content) as JsonObject))
            .ToList();
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (_, root) in files.Where(file => (string?)file.Root?["kind"] == "text"))
        {
            if (root?["locale"]?.GetValue<string>() is { } locale && root["texts"] is JsonObject entries)
            {
                var target = texts.TryGetValue(locale, out var existing) ? existing : texts[locale] = new(StringComparer.Ordinal);
                foreach (var (key, value) in entries)
                {
                    if (value is JsonValue text && text.TryGetValue<string>(out var line))
                    {
                        target[key] = line;
                    }
                }
            }
        }

        Dictionary<string, string>? TextsOf(JsonNode? reference)
        {
            // A property such as a scenario's title is plain text, not a text reference.
            if (reference is not JsonObject target || target["textKey"] is not JsonValue value || !value.TryGetValue<string>(out var key))
            {
                return null;
            }

            var found = texts.Where(locale => locale.Value.ContainsKey(key)).ToDictionary(locale => locale.Key, locale => locale.Value[key], StringComparer.Ordinal);
            return found.Count > 0 ? found : null;
        }

        return [.. files
            .Where(file => file.Root?["id"] is JsonValue id && id.TryGetValue<string>(out _))
            .Select(file => new StudioResourceActivityItem(
                file.Root!["id"]!.GetValue<string>(),
                status(file.Path),
                TextsOf(file.Root["label"] ?? file.Root["title"]),
                TextsOf(file.Root["description"])))];
    }

    /// <summary>Proposed: the locale a text resource holds, which Studio names it by, as the language's name.</summary>
    private static string? TextLocale(StudioFile file)
    {
        try
        {
            return (JsonNode.Parse(file.Bytes) as JsonObject)?["locale"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Proposed: Studio names a resource that has a label by it: the text of its <c>label</c>, as an entity, a role or
    /// an identity provider has, or of the <c>title</c> of a page or a site, in each locale that holds its key. A
    /// resource with no label, which only authors see, has no titles, and Studio names it by the name its author gave.
    /// </summary>
    private static Dictionary<string, string>? ResourceTitles(
        StudioFile file,
        Dictionary<string, Dictionary<string, string>> texts)
    {
        string? key;
        try
        {
            var root = JsonNode.Parse(file.Bytes) as JsonObject;
            key = (root?["label"] ?? root?["title"])?["textKey"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }

        if (key is null)
        {
            return null;
        }

        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (locale, entries) in texts)
        {
            if (entries.TryGetValue(key, out var title))
            {
                titles[locale] = title;
            }
        }

        return titles.Count > 0 ? titles : null;
    }

    private static async Task<IResult> ResourceReferencesAsync(
        string application,
        string id,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (FindResource(folder, id) is not { } found)
        {
            return NotFound(NoResource);
        }

        var edges = StudioReferences.Compile(folder, packages, modules).Edges;
        return Results.Ok(new StudioReferencesResponse(
            StudioReferences.Uses(edges.Where(edge => edge.From.Id == found.Id)),
            StudioReferences.UsedBy(edges.Where(edge => edge.To is ResourceNode target && target.Id == found.Id))));
    }

    private static async Task<IResult> TextReferencesAsync(
        string application,
        string key,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var usedBy = StudioReferences.Compile(folder, packages, modules).UsedBy(new TextKeyNode(key));
        if (usedBy.Count == 0)
        {
            return NotFound(NoTextKey);
        }

        return Results.Ok(new StudioTextReferencesResponse(StudioReferences.UsedBy(usedBy)));
    }

    private static async Task<IResult> GetAsync(string application, string id, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (FindResource(folder, id) is not { } found)
        {
            return NotFound(NoResource);
        }

        using var content = JsonDocument.Parse(found.Bytes);
        httpContext.Response.Headers.ETag = $"\"{found.Version}\"";
        return Results.Ok(new StudioResourceResponse(
            found.Id.ToString("D"), found.Kind, found.Name, found.File, found.Version, content.RootElement.Clone()));
    }

    private static async Task<IResult> ModuleResourceAsync(
        string application,
        string module,
        string id,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var (found, notFound) = FindModuleFile(
            ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules), modules, module, id);
        if (found is null)
        {
            return notFound!;
        }

        return Results.Ok(StudioModuleLayers.Read(folder, module, found));
    }

    /// <summary>One file of an installed module version: its file id, kind, name, path, text and parsed root.</summary>
    internal sealed record ModuleFile(
        Guid Id, string Kind, string? Name, string Path, string Text, JsonElement Root, InstalledModule Module);

    // The file with this id, written in the file, in the module version the compile pins, or the 404 problem. The id is not
    // the one derived for the application. The module's manifest is never a resource.
    internal static (ModuleFile? File, IResult? NotFound) FindModuleFile(
        CompilationResult compiled, ModuleCatalog modules, string module, string id)
    {
        var pin = compiled.ModulePins.FirstOrDefault(pin => string.Equals(pin.Id, module, StringComparison.Ordinal));
        var installed = pin is null
            ? null
            : modules.Modules.FirstOrDefault(item =>
                item.Manifest.Id == pin.Id && item.Manifest.Version == pin.Version);
        if (installed is null)
        {
            return (null, NotFound("The application uses no module with this id."));
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var guid))
        {
            return (null, NotFound(NoModuleResource));
        }

        foreach (var file in installed.Files.Where(file => file.Path != ModuleCatalog.ManifestFileName))
        {
            try
            {
                using var document = JsonDocument.Parse(file.Content);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                    || !root.TryGetProperty("id", out var fileId) || fileId.ValueKind != JsonValueKind.String
                    || !Guid.TryParseExact(fileId.GetString(), "D", out var fileGuid) || fileGuid != guid)
                {
                    continue;
                }

                var name = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
                    ? nameProperty.GetString()
                    : null;
                return (new ModuleFile(guid, kind.GetString()!, name, file.Path, file.Content, root.Clone(), installed), null);
            }
            catch (JsonException)
            {
            }
        }

        return (null, NotFound(NoModuleResource));
    }

    private static async Task<IResult> ModuleResourceSaveAsync(
        string application,
        string module,
        string id,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var (found, notFound) = FindModuleFile(
            ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules), modules, module, id);
        if (found is null)
        {
            return notFound!;
        }

        var request = httpContext.Request;
        var hasIfMatch = request.Headers.IfMatch.Count > 0;
        var hasIfNoneMatch = request.Headers.IfNoneMatch.Count > 0;
        if (!hasIfMatch && !hasIfNoneMatch)
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                "The save needs the layer's version in If-Match, or If-None-Match: * when there is no layer.");
        }

        if (hasIfMatch && hasIfNoneMatch)
        {
            return Problem(StatusCodes.Status400BadRequest, "Send If-Match or If-None-Match, not both.");
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        JsonDocument body;
        try
        {
            body = await JsonDocument.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The body is not a resource.");
        }

        using (body)
        {
            if (body.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Problem(StatusCodes.Status400BadRequest, "The body is not a resource.");
            }

            if (found.Kind == ResourceKinds.Scenario || found.Name is null)
            {
                return Problem(StatusCodes.Status400BadRequest, "A layer cannot change this resource.");
            }

            if (LayerChecker.KindsByName(found.Module)[found.Name].Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return Problem(StatusCodes.Status400BadRequest, "The module has more than one resource with this name.");
            }

            var difference = LayerDifference.Compare(found.Kind, found.Root, body.RootElement);
            if (difference.Refusal is not null)
            {
                return Problem(StatusCodes.Status400BadRequest, "The edit cannot be saved as a layer.");
            }

            if (found.Kind == ResourceKinds.Contract && difference.Changes is not null)
            {
                var layer = new LayerResource
                {
                    Id = Guid.Empty,
                    Kind = "layer",
                    FormatVersion = 1,
                    Target = $"{module}/{found.Name}",
                    Changes = JsonSerializer.SerializeToElement(difference.Changes),
                };
                var problems = new List<Diagnostic>();
                ContractLayerChecker.CheckAdditions(found.Root, layer, problems);
                if (problems.Any(problem => problem.Code == DiagnosticCodes.ContractLayerNotAddition))
                {
                    return Problem(StatusCodes.Status400BadRequest, "A layer on a contract only adds states and transitions.");
                }
            }

            // A tag other than * in If-None-Match matches no state of the layer, so the save is stale.
            if (hasIfNoneMatch && request.Headers.IfNoneMatch.ToString() != "*")
            {
                return Problem(StatusCodes.Status409Conflict, StaleLayer);
            }

            var outcome = await StudioWrites.WriteLayerAsync(
                writeLock,
                folder,
                module,
                found.Name,
                difference.Changes,
                hasIfMatch ? request.Headers.IfMatch.ToString() : null,
                hasIfNoneMatch,
                httpContext.RequestAborted);
            switch (outcome.Refusal)
            {
                case StudioWriteRefusal.Unavailable:
                    return Problem(StatusCodes.Status503ServiceUnavailable, WriteLockUnavailable);
                case StudioWriteRefusal.UnsafePath:
                    return Problem(StatusCodes.Status400BadRequest, "The file name leads outside the application folder.");
                case not null:
                    return Problem(StatusCodes.Status409Conflict, StaleLayer);
            }

            if (outcome.Version is not null)
            {
                httpContext.Response.Headers.ETag = $"\"{outcome.Version}\"";
            }

            return Results.Ok(new StudioModuleSaveResponse(
                outcome.File, outcome.Version, StudioDiagnostics.Compile(folder, packages, modules)));
        }
    }

    private const string StaleLayer = "The layer has changed since this version, or it exists already. Read it again.";

    private static async Task<IResult> DiagnosticsAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        // The compile step of activation, before it touches a database. The folder is read from disk as
        // activation reads it, not through the filtered list of the other routes.
        return Results.Ok(new StudioDiagnosticsResponse(StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private static async Task<IResult> NamesAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        return Results.Ok(ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules).Names);
    }

    private static async Task<IResult> PolicyMatrixAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        return Results.Ok(ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules).PolicyMatrix);
    }

    private static async Task<IResult> ReleasesAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        TenantDatabase database)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        // A folder without a readable manifest id has never been activated, so its history is empty.
        var applicationId = StudioFolder.List(folder).FirstOrDefault(file => file.File == "application.json")?.Id;
        var compiled = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules);
        var folderContentHash = compiled.ContentHash;
        if (applicationId is not { } id)
        {
            return Results.Ok(new StudioReleaseHistoryResponse(null, folderContentHash, []));
        }

        var cancellationToken = httpContext.RequestAborted;
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var active = await store.FindByApplicationIdAsync(id, cancellationToken);
        var activations = await store.ListActivationsAsync(id, cancellationToken);

        // A release can be in the history more than once, always with one number, so the first row of it has it.
        var activeRelease = active is null
            ? null
            : activations.FirstOrDefault(row => row.ReleaseId == active.ReleaseId) is { } current
                ? new StudioActiveRelease(current.ReleaseId, current.Number, current.ContentHash)
                : null;
        var names = await UserProfiles.FindDisplayNamesAsync(
            await database.GetConnectionAsync(cancellationToken), [.. activations.Select(row => row.Actor)], cancellationToken);
        var manifests = await store.GetManifestsAsync([.. activations.Select(row => row.ReleaseId).Distinct()], cancellationToken);
        // Proposed: the relaxed points of an off-standard release carry their words, from the module files that release
        // stores, read once for each such release.
        var offStandard = manifests.Where(pair => RelaxedPoints(pair.Value).Count > 0).Select(pair => pair.Key).ToList();
        var moduleFiles = offStandard.Count == 0
            ? []
            : (await (await database.GetConfigurationAsync(cancellationToken)).ReleaseResources
                .AsNoTracking()
                .Where(resource => offStandard.Contains(resource.ReleaseId) && resource.Path.StartsWith("modules/"))
                .Select(resource => new { resource.ReleaseId, resource.Path, resource.Content })
                .ToListAsync(cancellationToken))
                .GroupBy(resource => resource.ReleaseId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyDictionary<string, string>)group.ToDictionary(resource => resource.Path, resource => resource.Content, StringComparer.Ordinal));
        var described = offStandard.ToDictionary(
            releaseId => releaseId,
            releaseId => StudioRelaxedPointLabels.Describe(
                RelaxedPoints(manifests[releaseId]),
                moduleFiles.GetValueOrDefault(releaseId) ?? new Dictionary<string, string>()));
        var items = activations
            .Select(row =>
            {
                var relaxed = described.GetValueOrDefault(row.ReleaseId) ?? [];
                return new StudioReleaseItem(
                    row.ReleaseId, row.Number, row.ContentHash, row.ActivatedAt, row.Actor, names.GetValueOrDefault(row.Actor) ?? row.ActorName, row.Label, row.Note, row.RequestId,
                    relaxed.Count > 0, relaxed);
            })
            .ToList();
        var changes = activeRelease is null || activeRelease.ContentHash == folderContentHash
            ? []
            : await LiveChangesAsync(database, folder, compiled, activeRelease.ReleaseId, cancellationToken);
        return Results.Ok(new StudioReleaseHistoryResponse(activeRelease, folderContentHash, items, changes));
    }

    private static async Task<IReadOnlyList<StudioReleaseChange>> LiveChangesAsync(
        TenantDatabase database, string folderPath, CompilationResult result, Guid releaseId, CancellationToken token)
    {
        // Proposed: the files that differ from the live release, as the Overview lists them. Both sides are the
        // canonical JSON a release stores, so a file that only moved its spaces does not count. A folder with errors
        // has no compiled files, so its own files are read and the modules' copies are left out on both sides.
        var withModules = result.Resources.Count > 0;
        var folderFiles = (withModules ? result.Resources : Axis.Configuration.Loading.ApplicationLoader.Load(folderPath).Resources)
            .ToDictionary(resource => resource.Path, resource => resource.Content, StringComparer.Ordinal);
        var context = await database.GetConfigurationAsync(token);
        var releaseFiles = await context.ReleaseResources
            .Where(resource => resource.ReleaseId == releaseId)
            .Select(resource => new { resource.Path, resource.Content })
            .ToListAsync(token);
        var live = releaseFiles
            .Where(file => withModules || !file.Path.StartsWith("modules/", StringComparison.Ordinal))
            .ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        var list = new List<StudioReleaseChange>();
        foreach (var (path, content) in folderFiles)
        {
            if (!live.TryGetValue(path, out var liveContent))
            {
                list.Add(new StudioReleaseChange(path, "added"));
            }
            else if (liveContent != content)
            {
                list.Add(new StudioReleaseChange(path, "changed"));
            }
        }

        // Proposed: a removed file is no longer in the folder, so Studio names it from the live release: its kind, its
        // name and its label in the release's own texts.
        var removed = live.Where(file => !folderFiles.ContainsKey(file.Key)).ToList();
        if (removed.Count > 0)
        {
            var releaseTexts = ReleaseTexts(live.Values);
            list.AddRange(removed.Select(file => RemovedChange(file.Key, file.Value, releaseTexts)));
        }
        return [.. list.OrderBy(change => change.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Proposed: what one release holds beyond its history row, for its page: the files that differ from the release
    /// numbered just below it, each named from the release that holds it; the modules it pins, each with its label and
    /// version and the version the previous release pinned; and the packages it pins. A module's own files are left out
    /// of the changes, as its version says what changed. The first release has no previous one, so it lists no changes.
    /// </summary>
    private static async Task<IResult> ReleaseAsync(
        string application,
        int number,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packageCatalog,
        TenantDatabase database)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.List(folder).FirstOrDefault(file => file.File == "application.json")?.Id is not { } applicationId)
        {
            return NotFound("The release was not found.");
        }

        var cancellationToken = httpContext.RequestAborted;
        var context = await database.GetConfigurationAsync(cancellationToken);
        var activations = await new ActiveReleaseStore(context).ListActivationsAsync(applicationId, cancellationToken);
        if (activations.FirstOrDefault(row => row.Number == number) is not { } release)
        {
            return NotFound("The release was not found.");
        }

        var previous = activations.Where(row => row.Number < number).MaxBy(row => row.Number);
        var store = new ActiveReleaseStore(context);
        var live = await store.FindByApplicationIdAsync(applicationId, cancellationToken);
        var liveRow = live is null || live.ReleaseId == release.ReleaseId ? null : activations.FirstOrDefault(row => row.ReleaseId == live.ReleaseId);
        // Proposed: what the page compares the release with: the live release, as a release that is not live is opened to
        // decide whether to activate it, so its changes are what activating it would change; or, for the live release,
        // the release numbered just below it, so its changes are what it brought. The first live release has neither.
        var baseRow = liveRow ?? previous;
        var ids = new[] { release.ReleaseId, baseRow?.ReleaseId }.OfType<Guid>().Distinct().ToArray();
        var files = await context.ReleaseResources
            .AsNoTracking()
            .Where(resource => ids.Contains(resource.ReleaseId))
            .Select(resource => new { resource.ReleaseId, resource.Path, resource.Content })
            .ToListAsync(cancellationToken);
        var current = files.Where(file => file.ReleaseId == release.ReleaseId).ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        var before = baseRow is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : files.Where(file => file.ReleaseId == baseRow.ReleaseId).ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);

        var changes = new List<StudioReleaseChange>();
        if (baseRow is not null)
        {
            var currentTexts = ReleaseTexts(current.Values);
            var beforeTexts = ReleaseTexts(before.Values);
            static bool Own(string path) => !path.StartsWith("modules/", StringComparison.Ordinal);
            foreach (var (path, content) in current.Where(file => Own(file.Key)))
            {
                if (!before.TryGetValue(path, out var old))
                {
                    changes.Add(RemovedChange(path, content, currentTexts) with { Change = "added" });
                }
                else if (old != content)
                {
                    changes.Add(RemovedChange(path, content, currentTexts) with { Change = "changed" });
                }
            }

            changes.AddRange(before.Where(file => Own(file.Key) && !current.ContainsKey(file.Key)).Select(file => RemovedChange(file.Key, file.Value, beforeTexts)));
        }

        var beforeModules = Modules(before).ToDictionary(module => module.Id, module => module.Version, StringComparer.Ordinal);
        var modules = Modules(current)
            .Select(module => module with { PreviousVersion = baseRow is null ? null : beforeModules.GetValueOrDefault(module.Id) ?? "" })
            .ToList();
        // Proposed: a package is named by the label of its installed manifest, as a module is by its own.
        var packageLabels = packageCatalog.Packages
            .GroupBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Manifest.Label, StringComparer.Ordinal);
        var pinned = await context.ReleasePackages
            .AsNoTracking()
            .Where(package => ids.Contains(package.ReleaseId))
            .Select(package => new { package.ReleaseId, package.PackageId, package.Version })
            .ToListAsync(cancellationToken);
        var beforePackages = pinned.Where(package => package.ReleaseId == baseRow?.ReleaseId).ToDictionary(package => package.PackageId, package => package.Version, StringComparer.Ordinal);
        var packages = pinned
            .Where(package => package.ReleaseId == release.ReleaseId)
            .OrderBy(package => package.PackageId, StringComparer.Ordinal)
            .Select(package => new StudioReleasePackage(
                package.PackageId,
                package.Version,
                baseRow is null ? null : beforePackages.GetValueOrDefault(package.PackageId) ?? "",
                packageLabels.GetValueOrDefault(package.PackageId)))
            .ToList();
        // Proposed: how the modules and packages differ from the previous release, which the changes list shows beside
        // the files: one added, removed, upgraded or downgraded, by its version.
        var partChanges = new List<StudioReleasePartChange>();
        if (baseRow is not null)
        {
            var beforeModuleRows = Modules(before).ToList();
            partChanges.AddRange(PartChanges("module", modules.Select(module => (module.Id, module.Label, module.Version)), beforeModuleRows.Select(module => (module.Id, module.Label, module.Version))));
            partChanges.AddRange(PartChanges(
                "package",
                packages.Select(package => (package.Id, package.Label, package.Version)),
                pinned.Where(package => package.ReleaseId == baseRow.ReleaseId).Select(package => (package.PackageId, packageLabels.GetValueOrDefault(package.PackageId), package.Version))));
        }

        // Proposed: whether the release can replace the live one, told before Activate… is sent. A module's tables hold
        // data once it is live, so a release that lacks a module the live release pins, or pins a version without some
        // of its entities or fields, is refused on activation (AXC0213). The same check runs here on the two stored
        // releases, and each module it names is listed with the live version and this release's. What depends on the
        // tenant at that moment, such as its bindings, is still checked only on activation.
        var blockers = new List<StudioReleaseBlocker>();
        if (liveRow is not null)
        {
            Axis.Configuration.Storage.ReleaseResource Row(Guid releaseId, KeyValuePair<string, string> file) =>
                new() { ReleaseId = releaseId, Path = file.Key, Content = file.Value };
            var liveFiles = files.Where(file => file.ReleaseId == liveRow.ReleaseId).ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
            var diagnostics = Axis.Data.ModuleUpgradeCheck.Check(
                [.. liveFiles.Select(file => Row(liveRow.ReleaseId, file))],
                [.. current.Select(file => Row(release.ReleaseId, file))]);
            var liveModules = Modules(liveFiles).ToDictionary(module => module.Id, StringComparer.Ordinal);
            var targetModules = modules.ToDictionary(module => module.Id, module => module.Version, StringComparer.Ordinal);
            blockers.AddRange(diagnostics
                .Select(diagnostic => diagnostic.File.Split('/') is { Length: > 2 } parts && parts[0] == "modules" ? parts[1] : null)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(id => new StudioReleaseBlocker(
                    id,
                    liveModules.GetValueOrDefault(id)?.Label,
                    liveModules.GetValueOrDefault(id)?.Version ?? "",
                    targetModules.GetValueOrDefault(id))));
        }

        return Results.Ok(new StudioReleaseDetail(
            number,
            baseRow?.Number,
            [.. changes.OrderBy(change => change.Path, StringComparer.Ordinal)],
            modules,
            packages,
            liveRow?.Number,
            blockers,
            partChanges,
            liveRow is not null));

        static IEnumerable<StudioReleasePartChange> PartChanges(
            string type, IEnumerable<(string Id, string? Label, string Version)> now, IEnumerable<(string Id, string? Label, string Version)> then)
        {
            var old = then.ToDictionary(part => part.Id, StringComparer.Ordinal);
            var current = now.ToDictionary(part => part.Id, StringComparer.Ordinal);
            foreach (var part in current.Values)
            {
                if (!old.TryGetValue(part.Id, out var was))
                {
                    yield return new StudioReleasePartChange(type, part.Id, part.Label, "added", part.Version, null);
                }
                else if (was.Version != part.Version)
                {
                    var change = CompareVersions(part.Version, was.Version) > 0 ? "upgraded" : "downgraded";
                    yield return new StudioReleasePartChange(type, part.Id, part.Label, change, part.Version, was.Version);
                }
            }

            foreach (var was in old.Values.Where(part => !current.ContainsKey(part.Id)))
            {
                yield return new StudioReleasePartChange(type, was.Id, was.Label, "removed", null, was.Version);
            }
        }

        static int CompareVersions(string left, string right) =>
            System.Version.TryParse(left.Split('-')[0], out var a) && System.Version.TryParse(right.Split('-')[0], out var b)
                ? a.CompareTo(b)
                : string.CompareOrdinal(left, right);

        static IEnumerable<StudioReleaseModule> Modules(Dictionary<string, string> releaseFiles) =>
            releaseFiles
                .Where(file => file.Key.StartsWith("modules/", StringComparison.Ordinal) && file.Key.EndsWith("/module.json", StringComparison.Ordinal))
                .Select(file => JsonNode.Parse(file.Value))
                .Where(node => node?["id"] is not null)
                .Select(node => new StudioReleaseModule(
                    node!["id"]!.GetValue<string>(),
                    node["label"]?.GetValue<string>(),
                    node["version"]?.GetValue<string>() ?? ""))
                .OrderBy(module => module.Label ?? module.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Proposed: the relaxed points of the folder's <c>application.json</c>, each in words and with the id of the module
    /// contract that holds it, read from the module copies the folder compiles with, so Settings lists them read-only and
    /// links each to the contract page, the one place a point is relaxed or its relax undone.
    /// </summary>
    private static async Task<IResult> RelaxedAsync(
        string application, HttpContext httpContext, StudioOptions options, PackageCatalog packages, ModuleCatalog modules)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var manifestPath = Path.Combine(folder, "application.json");
        var manifest = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath, httpContext.RequestAborted) : null;
        var compiled = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules);
        var files = compiled.Resources
            .Where(resource => resource.Path.StartsWith("modules/", StringComparison.Ordinal))
            .ToDictionary(resource => resource.Path, resource => resource.Content, StringComparer.Ordinal);
        return Results.Ok(StudioRelaxedPointLabels.Describe(RelaxedPoints(manifest), files));
    }

    /// <summary>Proposed: the texts of each text resource of a release, by its locale, read from its canonical JSON.</summary>
    private static Dictionary<string, Dictionary<string, string>> ReleaseTexts(IEnumerable<string> contents)
    {
        var byLocale = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var content in contents)
        {
            if (JsonNode.Parse(content) is JsonObject root
                && root["kind"]?.GetValue<string>() == "text"
                && root["locale"]?.GetValue<string>() is { } locale
                && root["texts"] is JsonObject entries)
            {
                var target = byLocale.TryGetValue(locale, out var existing)
                    ? existing
                    : byLocale[locale] = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in entries)
                {
                    if (value is JsonValue text && text.TryGetValue<string>(out var line))
                    {
                        target[key] = line;
                    }
                }
            }
        }

        return byLocale;
    }

    /// <summary>
    /// Proposed: a removed file as the live release holds it: its kind, its name and its label in each locale, as
    /// <see cref="ResourceTitles"/> names a resource of the folder. A text resource is named by its locale.
    /// </summary>
    private static StudioReleaseChange RemovedChange(
        string path, string content, Dictionary<string, Dictionary<string, string>> texts)
    {
        if (JsonNode.Parse(content) is not JsonObject root)
        {
            return new StudioReleaseChange(path, "removed");
        }

        var kind = root["kind"]?.GetValue<string>();
        var name = root["name"]?.GetValue<string>();
        var key = (root["label"] ?? root["title"])?["textKey"]?.GetValue<string>();
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (key is not null)
        {
            foreach (var (locale, entries) in texts)
            {
                if (entries.TryGetValue(key, out var title))
                {
                    titles[locale] = title;
                }
            }
        }

        return new StudioReleaseChange(
            path,
            "removed",
            kind,
            name,
            titles.Count > 0 ? titles : null,
            kind == "text" ? root["locale"]?.GetValue<string>() : null);
    }

    // The relax entries of a stored application.json, in manifest order. A missing manifest has none.
    internal static List<StudioRelaxedPoint> RelaxedPoints(string? manifest) =>
        manifest is null
            ? []
            : ApplicationLoader.Load([new ResourceContent("application.json", manifest)]).Application?.Modules
                .SelectMany(module => module.Relax.Select(entry => new StudioRelaxedPoint(module.Id, entry.Point, entry.Reason ?? "")))
                .ToList() ?? [];

    private static async Task<IResult> PatchAsync(
        string application,
        string id,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (FindResource(folder, id) is not { } found)
        {
            return NotFound(NoResource);
        }

        var request = httpContext.Request;
        if (request.Headers.IfMatch.Count == 0)
        {
            return Problem(StatusCodes.Status428PreconditionRequired, "The patch needs the version it was made from in If-Match.");
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json-patch+json", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json-patch+json.");
        }

        JsonPatch patch;
        try
        {
            patch = (await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted)).Deserialize<JsonPatch>()
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The body is not a valid JSON Patch.");
        }

        var outcome = await StudioWrites.PatchAsync(
            writeLock, folder, found.Id, patch, request.Headers.IfMatch.ToString(), httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, WriteLockUnavailable);
            case StudioWriteRefusal.NotFound:
                return NotFound(NoResource);
            case StudioWriteRefusal.DoesNotApply:
                return Problem(StatusCodes.Status400BadRequest, "The patch does not apply to the resource.");
            case StudioWriteRefusal.ChangesIdOrKind:
                return Problem(StatusCodes.Status400BadRequest, "A patch may not change id or kind.");
            case StudioWriteRefusal.Stale:
                return Problem(StatusCodes.Status409Conflict, "The resource has changed since this version. Read it again.");
        }

        httpContext.Response.Headers.ETag = $"\"{outcome.Version}\"";
        return Results.Ok(new StudioPatchResponse(outcome.Version!, StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private static async Task<IResult> CreateAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        JsonObject content;
        try
        {
            content = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The body is not a resource.");
        }

        if (StudioFileNames.NewFile(content, out var id, out var problem) is not { } file)
        {
            return Problem(StatusCodes.Status400BadRequest, problem!);
        }

        var outcome = await StudioWrites.CreateAsync(writeLock, folder, id, file, content, httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Exists:
                return Problem(StatusCodes.Status409Conflict, "A resource with this id or file already exists.");
            case StudioWriteRefusal.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, WriteLockUnavailable);
            case not null:
                return Problem(StatusCodes.Status400BadRequest, "The file name leads outside the application folder.");
        }

        var location = $"/api/studio/apps/{Uri.EscapeDataString(application)}/resources/{id:D}";
        httpContext.Response.Headers.ETag = $"\"{outcome.Version}\"";
        return Results.Created(location, new StudioCreateResponse(
            id.ToString("D"),
            (string)content["kind"]!,
            (string)content["name"]!,
            file,
            outcome.Version!,
            StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private static async Task<IResult> ChangesAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        // The shape of the list: an object with only `changes`, holding 1 to 20 changes, each of them exactly
        // { create } or { patch, id, version? }.
        JsonArray list;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
            list = body.Count == 1 && body["changes"] is JsonArray array ? array : throw new JsonException();
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, BadChanges);
        }

        if (list.Count is < 1 or > MaxChanges || list.Any(item => !IsChangeShape(item)))
        {
            return Problem(StatusCodes.Status400BadRequest, BadChanges);
        }

        var items = list.Select(item => (JsonObject)item!).ToList();
        foreach (var item in items.Where(item => item.ContainsKey("patch")))
        {
            if (!Guid.TryParseExact((string)item["id"]!, "D", out var patchId)
                || StudioFolder.List(folder).All(file => file.Id != patchId))
            {
                return NotFound(NoResource);
            }
        }

        if (items.Any(item => item.ContainsKey("patch") && !item.ContainsKey("version")))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, "A patch change needs the version it was made from.");
        }

        var changes = new List<StudioChange>(items.Count);
        foreach (var item in items)
        {
            if (item["create"] is JsonObject content)
            {
                if (StudioFileNames.NewFile(content, out var id, out var problem) is not { } file)
                {
                    return Problem(StatusCodes.Status400BadRequest, problem!);
                }

                changes.Add(new StudioCreateChange(id, file, content));
                continue;
            }

            JsonPatch patch;
            try
            {
                patch = item["patch"].Deserialize<JsonPatch>() ?? throw new JsonException();
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                return Problem(StatusCodes.Status400BadRequest, "The body is not a valid JSON Patch.");
            }

            if (item["version"] is not JsonValue version || !version.TryGetValue<string>(out var text))
            {
                return Problem(StatusCodes.Status400BadRequest, BadChanges);
            }

            changes.Add(new StudioPatchChange(Guid.ParseExact((string)item["id"]!, "D"), patch, text));
        }

        if (changes.Select(change => change.Id).Distinct().Count() != changes.Count
            || changes.OfType<StudioCreateChange>().Select(change => change.File).Distinct(StringComparer.Ordinal).Count()
                != changes.OfType<StudioCreateChange>().Count())
        {
            return Problem(StatusCodes.Status400BadRequest, "Each change must name a different resource and file.");
        }

        var outcome = await StudioWrites.ChangesAsync(writeLock, folder, changes, httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, WriteLockUnavailable);
            case StudioWriteRefusal.DoesNotApply:
                return Problem(StatusCodes.Status400BadRequest, "The patch does not apply to the resource.");
            case StudioWriteRefusal.ChangesIdOrKind:
                return Problem(StatusCodes.Status400BadRequest, "A patch may not change id or kind.");
            case StudioWriteRefusal.UnsafePath:
                return Problem(StatusCodes.Status400BadRequest, "The file name leads outside the application folder.");
            case StudioWriteRefusal.Exists:
                return Problem(StatusCodes.Status409Conflict, "A resource with this id or file already exists.");
            case not null:
                // Stale, or a patched resource that is gone under the lock.
                return Problem(StatusCodes.Status409Conflict, "The resource has changed since this version. Read it again.");
        }

        return Results.Ok(new StudioChangesResponse(
            [.. changes.Select((change, index) => new StudioChangeItem(change.Id.ToString("D"), outcome.Versions![index]))],
            StudioDiagnostics.Compile(folder, packages, modules)));
    }

    // A change is exactly { create: object } or { patch, id: string, version? }, with no other key.
    private static bool IsChangeShape(JsonNode? node)
    {
        if (node is not JsonObject change)
        {
            return false;
        }

        if (change.ContainsKey("create"))
        {
            return change.Count == 1 && change["create"] is JsonObject;
        }

        return change.ContainsKey("patch")
            && change["id"] is JsonValue id && id.TryGetValue<string>(out _)
            && change.All(pair => pair.Key is "patch" or "id" or "version");
    }

    private const int MaxChanges = 20;

    private const string BadChanges = "The body is not a valid list of changes.";

    private static async Task<IResult> DeleteAsync(
        string application,
        string id,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (FindResource(folder, id) is not { } found)
        {
            return NotFound(NoResource);
        }

        var request = httpContext.Request;
        if (request.Headers.IfMatch.Count == 0)
        {
            return Problem(StatusCodes.Status428PreconditionRequired, "The delete needs the version it was read at in If-Match.");
        }

        var outcome = await StudioWrites.DeleteAsync(
            writeLock, folder, found.Id, request.Headers.IfMatch.ToString(), httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, WriteLockUnavailable);
            case StudioWriteRefusal.NotFound:
                return NotFound(NoResource);
            case StudioWriteRefusal.Manifest:
                return Problem(StatusCodes.Status400BadRequest, "The manifest cannot be created or deleted.");
            case StudioWriteRefusal.Stale:
                return Problem(StatusCodes.Status409Conflict, "The resource has changed since this version. Read it again.");
        }

        return Results.Ok(new StudioDeleteResponse(StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private static async Task<IResult> TextWriteAsync(
        string application,
        string key,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) is not JsonObject { Count: 1 } root
                || root["texts"] is not JsonObject entries)
            {
                throw new JsonException();
            }

            foreach (var (locale, value) in entries)
            {
                if (value is not JsonValue text || !text.TryGetValue<string>(out var content) || !texts.TryAdd(locale, content))
                {
                    throw new JsonException();
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Problem(StatusCodes.Status400BadRequest, InvalidTextWrite);
        }

        var locales = StudioWrites.TextFiles(folder);
        if (locales.Count != texts.Count || locales.Any(file => !texts.ContainsKey(file.Locale)))
        {
            return Problem(StatusCodes.Status400BadRequest, InvalidTextWrite);
        }

        var outcome = await StudioWrites.WriteTextAsync(writeLock, folder, key, texts, httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, WriteLockUnavailable);
            case StudioWriteRefusal.LocalesChanged:
                return Problem(StatusCodes.Status400BadRequest, InvalidTextWrite);
            case StudioWriteRefusal.TextsNotObject:
                return Problem(StatusCodes.Status409Conflict, "A text resource has no texts object.");
            case not null:
                return Problem(StatusCodes.Status400BadRequest, "The file name leads outside the application folder.");
        }

        return Results.Ok(new StudioTextWriteResponse(outcome.Resources, StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private const string InvalidTextWrite = "The body is not a valid text write.";

    internal const string WriteLockUnavailable = "The write lock could not be taken. Try again.";

    internal const string NoResource = "The application has no resource with this id.";

    private const string NoModuleResource = "The module has no resource with this id.";

    private const string NoTextKey = "The application has no text key with this name.";

    private static StudioFile? FindResource(string folder, string id) =>
        id is { Length: 36 } && Guid.TryParseExact(id, "D", out var guid)
            ? StudioFolder.List(folder).FirstOrDefault(file => file.Id == guid)
            : null;

    // The session and role checks every route shares: the problem that refuses the request, or null. A route that
    // publishes also needs the publisher role, which is checked right after the author role.
    internal static async Task<IResult?> RefuseAsync(HttpContext httpContext, StudioOptions options, bool publisher = false)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (!options.Allows(user.Roles))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You do not hold the author role.");
        }

        return publisher && !options.AllowsPublish(user.Roles)
            ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You do not hold the publisher role.")
            : null;
    }

    // The checks the application routes share: the folder, or the problem that refused the request.
    internal static async Task<(string? Folder, IResult? Refusal)> FindFolderAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        bool publisher = false)
    {
        if (await RefuseAsync(httpContext, options, publisher) is { } refusal)
        {
            return (null, refusal);
        }

        return StudioFolder.Find(ForRequest(httpContext, options), application) is { } folder
            ? (folder, null)
            : (null, NotFound("No Studio folder holds an application with this name."));
    }

    // The options of this request: in a cell, the applications folder of the request's tenant alone.
    internal static StudioOptions ForRequest(HttpContext httpContext, StudioOptions options) =>
        options.ForTenant(httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current?.TenantId);

    internal static IResult NotFound(string title) => Problem(StatusCodes.Status404NotFound, title);

    internal static IResult Problem(int statusCode, string title) => Results.Problem(statusCode: statusCode, title: title);
}

/// <summary>The applications of the Studio folders, in folder order.</summary>
internal sealed record StudioApplicationListResponse(IReadOnlyList<StudioApplicationItem> Items);

/// <summary>One application in the list: the <c>name</c> in its manifest.</summary>
internal sealed record StudioApplicationItem(
    string Name,
    IReadOnlyDictionary<string, string>? Titles = null,
    IReadOnlyDictionary<string, string>? Descriptions = null);

/// <summary>The resources of a Studio folder, sorted by file.</summary>
internal sealed record StudioResourceListResponse(IReadOnlyList<StudioResourceItem> Items);

/// <summary>
/// One resource in the list. <c>Version</c> is the lower-case SHA-256 hex of the file's bytes, and <c>UsedBy</c>
/// is the number of references that point at the resource. Proposed: <c>Titles</c> is its label in each locale, and
/// <c>Locale</c> the locale a text resource holds, and <c>Descriptions</c> its description in each locale.
/// </summary>
internal sealed record StudioResourceItem(
    string Id,
    string Kind,
    string? Name,
    string File,
    string Version,
    int UsedBy,
    IReadOnlyDictionary<string, string>? Titles = null,
    string? Locale = null,
    IReadOnlyDictionary<string, string>? Descriptions = null);

/// <summary>Proposed: the answer of the resource activity route.</summary>
internal sealed record StudioResourceActivityResponse(IReadOnlyList<StudioResourceActivityItem> Items);

/// <summary>
/// Proposed: one resource's <c>Status</c> against the live release, <c>new</c>, <c>changed</c> or <c>published</c>. A module's
/// resource also has its <c>Titles</c> and <c>Descriptions</c> by locale, as the resource list has them for the folder's.
/// </summary>
internal sealed record StudioResourceActivityItem(
    string Id,
    string Status,
    IReadOnlyDictionary<string, string>? Titles = null,
    IReadOnlyDictionary<string, string>? Descriptions = null);

/// <summary>One resource with its JSON content.</summary>
internal sealed record StudioResourceResponse(string Id, string Kind, string? Name, string File, string Version, JsonElement Content);

/// <summary>A module resource with the application's layer applied, the layer, its marks and the module elements it removed.</summary>
internal sealed record StudioModuleResourceResponse(
    string Id,
    string Kind,
    string? Name,
    string File,
    string Version,
    JsonElement Content,
    StudioModuleLayer? Layer,
    IReadOnlyList<StudioMark> Marks,
    IReadOnlyList<StudioRemovedElement> Removed);

/// <summary>The layer file that applied to a module resource, with the SHA-256 of its bytes.</summary>
internal sealed record StudioModuleLayer(string Id, string File, string Version);

/// <summary>An element or property that a layer added or changed, as a JSON Pointer into the content, with the mark <c>added</c> or <c>changed</c>.</summary>
internal sealed record StudioMark(string Path, string Mark);

/// <summary>A module element that a layer removed: the pointer of its list in the content, its key and the module's element.</summary>
internal sealed record StudioRemovedElement(string List, string Key, JsonElement Content);

/// <summary>The compile diagnostics of a Studio folder, in the compiler's order.</summary>
internal sealed record StudioDiagnosticsResponse(IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a patch: the file's new version and the folder's compile diagnostics.</summary>
internal sealed record StudioPatchResponse(string Version, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a module resource save: the layer's file and version, both null when no layer is left, and the folder's compile diagnostics.</summary>
internal sealed record StudioModuleSaveResponse(string? File, string? Version, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a create: the new resource's identity and version, and the folder's compile diagnostics.</summary>
internal sealed record StudioCreateResponse(
    string Id, string Kind, string Name, string File, string Version, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a batch of changes: the new version of each file in request order, and the folder's compile diagnostics.</summary>
internal sealed record StudioChangesResponse(IReadOnlyList<StudioChangeItem> Items, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>One change of the answer: the resource's <c>id</c> and the new <c>version</c> of its file.</summary>
internal sealed record StudioChangeItem(string Id, string Version);

/// <summary>The answer to a delete: the folder's compile diagnostics, which show the references it broke.</summary>
internal sealed record StudioDeleteResponse(IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a text write: each text resource's new version and the folder's compile diagnostics.</summary>
internal sealed record StudioTextWriteResponse(IReadOnlyList<StudioTextVersion> Resources, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>
/// The release history of an application, newest first. <c>Active</c> is the active release, or null when there
/// is none, and <c>FolderContentHash</c> is the hash the folder compiles to now, or null when it has errors.
/// </summary>
internal sealed record StudioReleaseHistoryResponse(
    StudioActiveRelease? Active,
    string? FolderContentHash,
    IReadOnlyList<StudioReleaseItem> Items,
    IReadOnlyList<StudioReleaseChange>? Changes = null);

/// <summary>
/// Proposed: a file of the folder that differs from the live release: <c>added</c>, <c>changed</c> or <c>removed</c>.
/// A removed file, which the folder no longer holds, also has the kind, the name, the label in each locale and, for a
/// text resource, the locale that the live release gives it, so Studio names it as it names a resource of the folder.
/// </summary>
internal sealed record StudioReleaseChange(
    string Path,
    string Change,
    string? Kind = null,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Titles = null,
    string? Locale = null);

/// <summary>
/// Proposed: one release's page: its number; the release it is compared with in <c>Previous</c>, the live one when it is
/// not live (<c>PreviousIsLive</c>), else the one just below it, or null for the first; the files that differ from that
/// release, the modules and the packages it pins.
/// </summary>
internal sealed record StudioReleaseDetail(
    int Number,
    int? Previous,
    IReadOnlyList<StudioReleaseChange> Changes,
    IReadOnlyList<StudioReleaseModule> Modules,
    IReadOnlyList<StudioReleasePackage> Packages,
    int? Live = null,
    IReadOnlyList<StudioReleaseBlocker>? Blockers = null,
    IReadOnlyList<StudioReleasePartChange>? PartChanges = null,
    bool PreviousIsLive = false);

/// <summary>
/// Proposed: a module or package that differs from the previous release: <c>added</c>, <c>removed</c>,
/// <c>upgraded</c> or <c>downgraded</c>, with its version now and before, each null when it had none.
/// </summary>
internal sealed record StudioReleasePartChange(string Type, string Id, string? Label, string Change, string? Version, string? PreviousVersion);

/// <summary>
/// Proposed: a module that stops a release from replacing the live one: its id, its label, the version the live release
/// pins, and the version this release pins, or null when it pins none.
/// </summary>
internal sealed record StudioReleaseBlocker(string Module, string? Label, string LiveVersion, string? Version);

/// <summary>
/// Proposed: a module a release pins: its id, its label, its version, and the version the previous release pinned,
/// empty when it pinned none, or null for the first release.
/// </summary>
internal sealed record StudioReleaseModule(string Id, string? Label, string Version, string? PreviousVersion = null);

/// <summary>Proposed: a package a release pins, its version and the previous release's, as for a module.</summary>
internal sealed record StudioReleasePackage(string Id, string Version, string? PreviousVersion = null, string? Label = null);

/// <summary>The active release: its id, its number and its content hash.</summary>
internal sealed record StudioActiveRelease(Guid ReleaseId, int Number, string ContentHash);

/// <summary>
/// One activation in the history. <c>Actor</c> is a user id, <c>startup</c>, <c>operator</c> or <c>unknown</c>. <c>ActorName</c> is
/// the name in the actor's user profile, else the one stored with the activation, else null.
/// </summary>
internal sealed record StudioReleaseItem(
    Guid ReleaseId,
    int Number,
    string ContentHash,
    DateTimeOffset ActivatedAt,
    string Actor,
    string? ActorName,
    string? Label,
    string? Note,
    Guid? RequestId,
    bool OffStandard,
    IReadOnlyList<StudioRelaxedPoint> Relaxed);

/// <summary>
/// A relax entry of a release's application: the module, the point it turns off and the reason. Proposed: the words
/// for people, from the module files the release stores: <c>ModuleTitle</c>, the module's label; <c>Part</c>, what
/// the point is (<c>transition</c>, <c>initial</c>, <c>deletable</c>, <c>validation</c>, <c>effect</c>,
/// <c>ceiling</c> or <c>others</c>); <c>EntityTitles</c>, the entity's label per locale; and <c>Titles</c>, the label
/// per locale of the transition, state, validation, effect or role slot it names. Each is null when the release has no
/// words for it.
/// </summary>
internal sealed record StudioRelaxedPoint(
    string Module,
    string Point,
    string Reason,
    string? ModuleTitle = null,
    string? Part = null,
    IReadOnlyDictionary<string, string>? EntityTitles = null,
    IReadOnlyDictionary<string, string>? Titles = null,
    string? ContractId = null);
