using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Server.Tenancy;
using Axis.Studio;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// The routes of an application's package files, the files under <c>packages/</c> of its working folder, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. <c>GET /api/studio/apps/{application}/package-files</c> answers
/// <c>{ items: [{ package, path, version }] }</c>, sorted by file. <c>GET .../packages/{package}/files/{**path}</c> answers
/// <c>{ package, path, version, content }</c> with the version as a quoted <c>ETag</c>. Both need the author role only.
/// <c>PUT</c> on that path takes <c>{ content }</c>, with <c>If-Match</c> to replace or <c>If-None-Match: *</c> to create, writes the
/// UTF-8 bytes of <c>content</c> as given and answers <c>{ version, diagnostics }</c>. <c>DELETE</c> removes the file when
/// <c>If-Match</c> holds its version and answers <c>{ diagnostics }</c>. A write also needs the developer role and the tenant's
/// application packages switch on. A write is checked in order: session (401), author role (403), developer role (403), the
/// switch (403), the application (404), the path (400 for <c>PUT</c>, 404 for <c>DELETE</c>), the missing file of a
/// <c>DELETE</c> (404), the precondition headers (428 for none, 400 for both on <c>PUT</c>), the content type (415) and the body (400). The
/// write lock (503) and the version (409) come last. A read answers 404 for a path that names no file. In a cell the <c>PUT</c> checks the tenant's storage
/// quota (403 <c>quota-exceeded</c>) right after the session, and the <c>DELETE</c> never does, see
/// <c>docs/reference/tenant-quotas.md#refusals</c>. Each write is one revision.
/// </summary>
internal static class StudioPackageFileEndpoints
{
    private const string NoPackageFile = "The application has no package file at this path.";

    private const string BadPath = "The package or the path is not a plain file path under packages/.";

    private const string StalePackageFile = "The package file has changed since this version, or it exists already. Read it again.";

    public static void MapStudioPackageFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/package-files", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/packages/{package}/files/{**path}", GetAsync);
        endpoints.MapPut("/api/studio/apps/{application}/packages/{package}/files/{**path}", PutAsync).WithStorageQuota();
        endpoints.MapDelete("/api/studio/apps/{application}/packages/{package}/files/{**path}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(string application, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        return Results.Ok(new StudioPackageFileListResponse(
            [.. StudioPackageFiles.List(folder).Select(item => new StudioPackageFileItem(item.Package, item.Path, item.Version))]));
    }

    private static async Task<IResult> GetAsync(
        string application, string package, string path, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioPackageFiles.File(package, path) is not { } file || StudioPackageFiles.Read(folder, file) is not { } bytes)
        {
            return StudioEndpoints.NotFound(NoPackageFile);
        }

        var version = Convert.ToHexStringLower(SHA256.HashData(bytes));
        httpContext.Response.Headers.ETag = $"\"{version}\"";
        return Results.Ok(new StudioPackageFileResponse(package, path, version, Encoding.UTF8.GetString(bytes)));
    }

    private static async Task<IResult> PutAsync(
        string application,
        string package,
        string path,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, developer: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioPackageFiles.File(package, path) is not { } file)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadPath);
        }

        var request = httpContext.Request;
        var hasIfMatch = request.Headers.IfMatch.Count > 0;
        var hasIfNoneMatch = request.Headers.IfNoneMatch.Count > 0;
        if (!hasIfMatch && !hasIfNoneMatch)
        {
            return StudioEndpoints.Problem(
                StatusCodes.Status428PreconditionRequired,
                "The save needs the file's version in If-Match, or If-None-Match: * when there is no file.");
        }

        if (hasIfMatch && hasIfNoneMatch)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "Send If-Match or If-None-Match, not both.");
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        string content;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
            content = body.Count == 1 && body["content"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : throw new JsonException();
        }
        catch (JsonException)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body must be an object with a string content.");
        }

        // A tag other than * in If-None-Match matches no state of the file, so the save is stale.
        if (hasIfNoneMatch && request.Headers.IfNoneMatch.ToString() != "*")
        {
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, StalePackageFile);
        }

        var outcome = await StudioWrites.WritePackageFileAsync(
            writeLock,
            await StudioEndpoints.RevisionsAsync(httpContext, options, folder),
            folder,
            file,
            Encoding.UTF8.GetBytes(content),
            hasIfMatch ? request.Headers.IfMatch.ToString() : null,
            hasIfNoneMatch,
            httpContext.RequestAborted);
        if (Refused(outcome) is { } refused)
        {
            return refused;
        }

        httpContext.Response.Headers.ETag = $"\"{outcome.Version}\"";
        return Results.Ok(new StudioPatchResponse(outcome.Version!, StudioDiagnostics.Compile(folder, packages, modules)));
    }

    private static async Task<IResult> DeleteAsync(
        string application,
        string package,
        string path,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, developer: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioPackageFiles.File(package, path) is not { } file || StudioPackageFiles.Read(folder, file) is null)
        {
            return StudioEndpoints.NotFound(NoPackageFile);
        }

        var request = httpContext.Request;
        if (request.Headers.IfMatch.Count == 0)
        {
            return StudioEndpoints.Problem(StatusCodes.Status428PreconditionRequired, "The delete needs the file's version in If-Match.");
        }

        var outcome = await StudioWrites.DeletePackageFileAsync(
            writeLock,
            await StudioEndpoints.RevisionsAsync(httpContext, options, folder),
            folder,
            file,
            request.Headers.IfMatch.ToString(),
            httpContext.RequestAborted);
        if (Refused(outcome) is { } refused)
        {
            return refused;
        }

        return Results.Ok(new StudioDeleteResponse(StudioDiagnostics.Compile(folder, packages, modules)));
    }

    // The problem that a refused write answers, or null when the write was made.
    private static IResult? Refused(StudioWriteOutcome outcome) => outcome.Refusal switch
    {
        null => null,
        StudioWriteRefusal.Unavailable => StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable),
        StudioWriteRefusal.Stale or StudioWriteRefusal.Exists => StudioEndpoints.Problem(StatusCodes.Status409Conflict, StalePackageFile),
        _ => StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadPath),
    };
}

/// <summary>The package files of a Studio folder, sorted by file.</summary>
internal sealed record StudioPackageFileListResponse(IReadOnlyList<StudioPackageFileItem> Items);

/// <summary>One package file in the list: its package, its path inside the package and the SHA-256 of its bytes as lower-case hex.</summary>
internal sealed record StudioPackageFileItem(string Package, string Path, string Version);

/// <summary>One package file with its text, read as UTF-8, and its version.</summary>
internal sealed record StudioPackageFileResponse(string Package, string Path, string Version, string Content);
