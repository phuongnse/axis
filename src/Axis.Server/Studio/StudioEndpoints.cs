using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Data.Profiles;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Json.Patch;
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
/// <c>modules/&lt;module&gt;/&lt;path&gt;</c> and no <c>ETag</c>. It answers 404 for an unknown application, a module the
/// application does not pin and an unknown resource, and never answers <c>module.json</c>. The page metadata route of
/// <see cref="StudioPageMetadataEndpoints"/> finds a module page with the same lookup.
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
/// <c>GET /api/studio/apps/{application}/releases</c> answers the application's release history in this tenant,
/// newest first, as <c>{ active, folderContentHash, items }</c>. <c>active</c> is the active release or null, and
/// <c>folderContentHash</c> is the content hash the folder compiles to now, or null when it has errors. It reads the
/// tenant database and changes nothing.
/// </summary>
internal static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps", AppsAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources/{id}", GetAsync);
        endpoints.MapGet("/api/studio/apps/{application}/modules/{module}/resources/{id}", ModuleResourceAsync);
        endpoints.MapPost("/api/studio/apps/{application}/resources", CreateAsync);
        endpoints.MapPost("/api/studio/apps/{application}/changes", ChangesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/diagnostics", DiagnosticsAsync);
        endpoints.MapGet("/api/studio/apps/{application}/names", NamesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/policy-matrix", PolicyMatrixAsync);
        endpoints.MapGet("/api/studio/apps/{application}/releases", ReleasesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources/{id}/references", ResourceReferencesAsync);
        endpoints.MapGet("/api/studio/apps/{application}/texts/{key}/references", TextReferencesAsync);
        endpoints.MapPatch("/api/studio/apps/{application}/resources/{id}", PatchAsync);
        endpoints.MapDelete("/api/studio/apps/{application}/resources/{id}", DeleteAsync);
        endpoints.MapPut("/api/studio/apps/{application}/texts/{key}", TextWriteAsync);
        endpoints.MapStudioPublishEndpoints();
        endpoints.MapStudioExpressionEndpoints();
        endpoints.MapStudioPageMetadataEndpoints();
        endpoints.MapStudioTemplateEndpoints();
        endpoints.MapStudioApplicationEndpoints();
    }

    private static async Task<IResult> AppsAsync(HttpContext httpContext, StudioOptions options)
    {
        if (await RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        return Results.Ok(new StudioApplicationListResponse(
            [.. StudioFolder.Applications(options).Select(name => DescribeApplication(options, name))]));
    }

    /// <summary>
    /// Proposed: an application of the list with its title and description per locale, from the `label` and
    /// `description` of its manifest and its text resources, so Studio lists it as the home page does. A key that no
    /// text holds, or a folder that does not load, leaves the name and no description.
    /// </summary>
    private static StudioApplicationItem DescribeApplication(StudioOptions options, string name)
    {
        if (StudioFolder.Find(options, name) is not { } folder)
        {
            return new StudioApplicationItem(name);
        }

        var loaded = Axis.Configuration.Loading.ApplicationLoader.Load(folder);
        var manifest = loaded.Application;
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in loaded.Texts)
        {
            titles[text.Locale] = manifest?.Label is { } label && text.Texts.TryGetValue(label.TextKey, out var title)
                ? title
                : name;
            if (manifest?.Description is { } description && text.Texts.TryGetValue(description.TextKey, out var line))
            {
                descriptions[text.Locale] = line;
            }
        }

        return new StudioApplicationItem(name, titles, descriptions.Count > 0 ? descriptions : null);
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
        var items = StudioFolder.List(folder)
            .Select(file => new StudioResourceItem(
                file.Id.ToString("D"), file.Kind, file.Name, file.File, file.Version, usedBy.GetValueOrDefault(file.Id)))
            .ToList();
        return Results.Ok(new StudioResourceListResponse(items));
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

        return Results.Ok(new StudioResourceResponse(
            found.Id.ToString("D"),
            found.Kind,
            found.Name,
            $"modules/{module}/{found.Path}",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(found.Text))),
            found.Root));
    }

    /// <summary>One file of an installed module version: its file id, kind, name, path, text and parsed root.</summary>
    internal sealed record ModuleFile(Guid Id, string Kind, string? Name, string Path, string Text, JsonElement Root);

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
                return (new ModuleFile(guid, kind.GetString()!, name, file.Path, file.Content, root.Clone()), null);
            }
            catch (JsonException)
            {
            }
        }

        return (null, NotFound(NoModuleResource));
    }

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
        var folderContentHash = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules).ContentHash;
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
        var items = activations
            .Select(row => new StudioReleaseItem(
                row.ReleaseId, row.Number, row.ContentHash, row.ActivatedAt, row.Actor, names.GetValueOrDefault(row.Actor) ?? row.ActorName, row.Label, row.Note, row.RequestId))
            .ToList();
        return Results.Ok(new StudioReleaseHistoryResponse(activeRelease, folderContentHash, items));
    }

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

        return StudioFolder.Find(options, application) is { } folder
            ? (folder, null)
            : (null, NotFound("No Studio folder holds an application with this name."));
    }

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
/// is the number of references that point at the resource.
/// </summary>
internal sealed record StudioResourceItem(string Id, string Kind, string? Name, string File, string Version, int UsedBy);

/// <summary>One resource with its JSON content.</summary>
internal sealed record StudioResourceResponse(string Id, string Kind, string? Name, string File, string Version, JsonElement Content);

/// <summary>The compile diagnostics of a Studio folder, in the compiler's order.</summary>
internal sealed record StudioDiagnosticsResponse(IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a patch: the file's new version and the folder's compile diagnostics.</summary>
internal sealed record StudioPatchResponse(string Version, IReadOnlyList<StudioDiagnostic> Diagnostics);

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
    StudioActiveRelease? Active, string? FolderContentHash, IReadOnlyList<StudioReleaseItem> Items);

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
    Guid? RequestId);
