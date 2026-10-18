using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Server.Http;
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
/// the folder, sorted by file, each with its <c>id</c>, <c>kind</c>, <c>name</c>, <c>file</c> and
/// <c>version</c>. <c>GET /api/studio/apps/{application}/resources/{id}</c> returns one resource with its
/// JSON <c>content</c>, and its <c>version</c> as a quoted <c>ETag</c>. The version is the SHA-256 of the
/// file's bytes. A request is checked in order: session (401), author role with Studio on (403),
/// application (404), then resource (404). The problem titles never contain text from the request.
/// Only real <c>*.json</c> files inside a listed folder are reachable.
/// <c>GET /api/studio/apps/{application}/diagnostics</c> compiles the folder in memory against the installed
/// extension packages and modules and answers every compile diagnostic. It touches no database and activates nothing.
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
/// </summary>
internal static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps", AppsAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources/{id}", GetAsync);
        endpoints.MapPost("/api/studio/apps/{application}/resources", CreateAsync);
        endpoints.MapGet("/api/studio/apps/{application}/diagnostics", DiagnosticsAsync);
        endpoints.MapPatch("/api/studio/apps/{application}/resources/{id}", PatchAsync);
        endpoints.MapDelete("/api/studio/apps/{application}/resources/{id}", DeleteAsync);
    }

    private static async Task<IResult> AppsAsync(HttpContext httpContext, StudioOptions options)
    {
        if (await RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        return Results.Ok(new StudioApplicationListResponse(
            [.. StudioFolder.Applications(options).Select(name => new StudioApplicationItem(name))]));
    }

    private static async Task<IResult> ListAsync(string application, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var items = StudioFolder.List(folder)
            .Select(file => new StudioResourceItem(file.Id.ToString("D"), file.Kind, file.Name, file.File, file.Version))
            .ToList();
        return Results.Ok(new StudioResourceListResponse(items));
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

    private static async Task<IResult> PatchAsync(
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
            folder, found.Id, patch, request.Headers.IfMatch.ToString(), httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
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
        ModuleCatalog modules)
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

        var outcome = await StudioWrites.CreateAsync(folder, id, file, content, httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Exists:
                return Problem(StatusCodes.Status409Conflict, "A resource with this id or file already exists.");
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

    private static async Task<IResult> DeleteAsync(
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

        var request = httpContext.Request;
        if (request.Headers.IfMatch.Count == 0)
        {
            return Problem(StatusCodes.Status428PreconditionRequired, "The delete needs the version it was read at in If-Match.");
        }

        var outcome = await StudioWrites.DeleteAsync(
            folder, found.Id, request.Headers.IfMatch.ToString(), httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.NotFound:
                return NotFound(NoResource);
            case StudioWriteRefusal.Manifest:
                return Problem(StatusCodes.Status400BadRequest, "The manifest cannot be created or deleted.");
            case StudioWriteRefusal.Stale:
                return Problem(StatusCodes.Status409Conflict, "The resource has changed since this version. Read it again.");
        }

        return Results.Ok(new StudioDeleteResponse(StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private const string NoResource = "The application has no resource with this id.";

    private static StudioFile? FindResource(string folder, string id) =>
        id is { Length: 36 } && Guid.TryParseExact(id, "D", out var guid)
            ? StudioFolder.List(folder).FirstOrDefault(file => file.Id == guid)
            : null;

    // The session and role checks every route shares: the problem that refuses the request, or null.
    private static async Task<IResult?> RefuseAsync(HttpContext httpContext, StudioOptions options)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        return options.Allows(user.Roles)
            ? null
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You do not hold the author role.");
    }

    // The checks the application routes share: the folder, or the problem that refused the request.
    private static async Task<(string? Folder, IResult? Refusal)> FindFolderAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options)
    {
        if (await RefuseAsync(httpContext, options) is { } refusal)
        {
            return (null, refusal);
        }

        return StudioFolder.Find(options, application) is { } folder
            ? (folder, null)
            : (null, NotFound("No Studio folder holds an application with this name."));
    }

    private static IResult NotFound(string title) => Problem(StatusCodes.Status404NotFound, title);

    private static IResult Problem(int statusCode, string title) => Results.Problem(statusCode: statusCode, title: title);
}

/// <summary>The applications of the Studio folders, in folder order.</summary>
internal sealed record StudioApplicationListResponse(IReadOnlyList<StudioApplicationItem> Items);

/// <summary>One application in the list: the <c>name</c> in its manifest.</summary>
internal sealed record StudioApplicationItem(string Name);

/// <summary>The resources of a Studio folder, sorted by file.</summary>
internal sealed record StudioResourceListResponse(IReadOnlyList<StudioResourceItem> Items);

/// <summary>One resource in the list. <c>Version</c> is the lower-case SHA-256 hex of the file's bytes.</summary>
internal sealed record StudioResourceItem(string Id, string Kind, string? Name, string File, string Version);

/// <summary>One resource with its JSON content.</summary>
internal sealed record StudioResourceResponse(string Id, string Kind, string? Name, string File, string Version, JsonElement Content);

/// <summary>The compile diagnostics of a Studio folder, in the compiler's order.</summary>
internal sealed record StudioDiagnosticsResponse(IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a patch: the file's new version and the folder's compile diagnostics.</summary>
internal sealed record StudioPatchResponse(string Version, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a create: the new resource's identity and version, and the folder's compile diagnostics.</summary>
internal sealed record StudioCreateResponse(
    string Id, string Kind, string Name, string File, string Version, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a delete: the folder's compile diagnostics, which show the references it broke.</summary>
internal sealed record StudioDeleteResponse(IReadOnlyList<StudioDiagnostic> Diagnostics);
