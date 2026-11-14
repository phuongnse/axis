using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Server.Mcp;
using Axis.Server.Tenancy;
using Axis.Server.Users;
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
/// <para>
/// With <c>?draft=&lt;id&gt;</c> every route lists, reads, writes or deletes the files of the signed-in author's draft instead
/// and leaves the working folder alone, with the same answers. The checks run in the same order up to the body. Then come the
/// write lock (503), the draft (404, also for another person's draft), the missing file of a read or a <c>DELETE</c> (404) and
/// the version (409), which is the version in the draft. A write is one commit on the draft's branch, and its diagnostics are
/// the draft's own.
/// </para>
/// </summary>
internal static class StudioPackageFileEndpoints
{
    internal const string NoPackageFile = "The application has no package file at this path.";

    internal const string BadPath = "The package or the path is not a plain file path under packages/.";

    internal const string StalePackageFile = "The package file has changed since this version, or it exists already. Read it again.";

    public static void MapStudioPackageFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/package-files", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/packages/{package}/files/{**path}", GetAsync);
        endpoints.MapPut("/api/studio/apps/{application}/packages/{package}/files/{**path}", PutAsync).WithStorageQuota();
        endpoints.MapDelete("/api/studio/apps/{application}/packages/{package}/files/{**path}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        string application, string? draft, HttpContext httpContext, StudioOptions options, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (draft is not null)
        {
            if (await DraftsAsync(httpContext, options, folder, writeLock) is not { } drafts)
            {
                return StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft);
            }

            var listed = await drafts.ListPackageFilesAsync(draft, httpContext.RequestAborted);
            return Refused(listed.Refusal) ?? Results.Ok(new StudioPackageFileListResponse(
                [.. listed.Value!.Select(item => new StudioPackageFileItem(item.Package, item.Path, item.Version))]));
        }

        return Results.Ok(new StudioPackageFileListResponse(
            [.. StudioPackageFiles.List(folder).Select(item => new StudioPackageFileItem(item.Package, item.Path, item.Version))]));
    }

    private static async Task<IResult> GetAsync(
        string application, string package, string path, string? draft, HttpContext httpContext, StudioOptions options, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioPackageFiles.File(package, path) is not { } file)
        {
            return StudioEndpoints.NotFound(NoPackageFile);
        }

        byte[] bytes;
        if (draft is not null)
        {
            if (await DraftsAsync(httpContext, options, folder, writeLock) is not { } drafts)
            {
                return StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft);
            }

            var read = await drafts.ReadPackageFileAsync(draft, file, httpContext.RequestAborted);
            if (Refused(read.Refusal) is { } refused)
            {
                return refused;
            }

            bytes = read.Value!;
        }
        else if (StudioPackageFiles.Read(folder, file) is { } found)
        {
            bytes = found;
        }
        else
        {
            return StudioEndpoints.NotFound(NoPackageFile);
        }

        var answer = Response(package, path, bytes);
        httpContext.Response.Headers.ETag = $"\"{answer.Version}\"";
        return Results.Ok(answer);
    }

    /// <summary>The answer that reads one package file: its text, read as UTF-8, and the SHA-256 of its bytes as lower-case hex.</summary>
    internal static StudioPackageFileResponse Response(string package, string path, byte[] bytes) =>
        new(package, path, Convert.ToHexStringLower(SHA256.HashData(bytes)), Encoding.UTF8.GetString(bytes));

    private static async Task<IResult> PutAsync(
        string application,
        string package,
        string path,
        string? draft,
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

        if (draft is not null)
        {
            return await PutDraftAsync(
                httpContext, options, packages, modules, writeLock, folder, draft, file, Encoding.UTF8.GetBytes(content), hasIfMatch, hasIfNoneMatch);
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
        if (Refused(outcome.Refusal, StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadPath)) is { } refused)
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
        string? draft,
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

        if (StudioPackageFiles.File(package, path) is not { } file || (draft is null && StudioPackageFiles.Read(folder, file) is null))
        {
            return StudioEndpoints.NotFound(NoPackageFile);
        }

        var request = httpContext.Request;
        if (request.Headers.IfMatch.Count == 0)
        {
            return StudioEndpoints.Problem(StatusCodes.Status428PreconditionRequired, "The delete needs the file's version in If-Match.");
        }

        if (draft is not null)
        {
            if (await DraftsAsync(httpContext, options, folder, writeLock) is not { } drafts)
            {
                return StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft);
            }

            var deleted = await drafts.DeletePackageFileAsync(draft, file, request.Headers.IfMatch.ToString(), httpContext.RequestAborted);
            if (Refused(deleted.Refusal) is { } notDeleted)
            {
                return notDeleted;
            }

            var (draftDiagnostics, error) = await DraftDiagnosticsAsync(httpContext, drafts, draft, packages, modules);
            return error ?? Results.Ok(new StudioDeleteResponse(draftDiagnostics!));
        }

        var outcome = await StudioWrites.DeletePackageFileAsync(
            writeLock,
            await StudioEndpoints.RevisionsAsync(httpContext, options, folder),
            folder,
            file,
            request.Headers.IfMatch.ToString(),
            httpContext.RequestAborted);
        if (Refused(outcome.Refusal, StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadPath)) is { } refused)
        {
            return refused;
        }

        return Results.Ok(new StudioDeleteResponse(StudioDiagnostics.Compile(folder, packages, modules)));
    }

    // The draft's package file write, after the checks it shares with the working folder's. The answer is the working folder's, with
    // the draft's version and diagnostics.
    private static async Task<IResult> PutDraftAsync(
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock,
        string folder,
        string draft,
        string file,
        byte[] bytes,
        bool hasIfMatch,
        bool hasIfNoneMatch)
    {
        if (await DraftsAsync(httpContext, options, folder, writeLock) is not { } drafts)
        {
            return StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft);
        }

        var written = await drafts.WritePackageFileAsync(
            draft,
            file,
            bytes,
            hasIfMatch ? httpContext.Request.Headers.IfMatch.ToString() : null,
            hasIfNoneMatch,
            httpContext.RequestAborted);
        if (Refused(written.Refusal) is { } refused)
        {
            return refused;
        }

        var (diagnostics, error) = await DraftDiagnosticsAsync(httpContext, drafts, draft, packages, modules);
        if (error is not null)
        {
            return error;
        }

        httpContext.Response.Headers.ETag = $"\"{written.Value}\"";
        return Results.Ok(new StudioPatchResponse(written.Value!, diagnostics!));
    }

    // The signed-in author's drafts on the folder, or null when the server keeps no revisions.
    private static async Task<StudioDrafts?> DraftsAsync(HttpContext httpContext, StudioOptions options, string folder, IStudioWriteLock writeLock)
    {
        var user = (await CurrentUsers.FindAsync(httpContext))!;
        return StudioDrafts.For(StudioEndpoints.ForRequest(httpContext, options).RevisionsPath, folder, user.Id, user.DisplayName, writeLock);
    }

    // The compile diagnostics of the draft as its branch now holds it, as an MCP draft write answers them, or the refusal.
    private static async Task<(IReadOnlyList<StudioDiagnostic>? Diagnostics, IResult? Error)> DraftDiagnosticsAsync(
        HttpContext httpContext, StudioDrafts drafts, string draft, PackageCatalog packages, ModuleCatalog modules)
    {
        var files = await drafts.FilesAsync(draft, httpContext.RequestAborted);
        return Refused(files.Refusal) is { } refused
            ? (null, refused)
            : (StudioDiagnostics.CompileFiles(McpReadTools.Contents(files.Value!), packages, modules), null);
    }

    // The problem that a refused write answers, or null when the write was made. A refusal that no case names answers
    // otherwise: a draft that is not found, unless the working folder's write gives another.
    private static IResult? Refused(StudioWriteRefusal? refusal, IResult? otherwise = null) => refusal switch
    {
        null => null,
        StudioWriteRefusal.Unavailable => StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable),
        StudioWriteRefusal.Stale or StudioWriteRefusal.Exists => StudioEndpoints.Problem(StatusCodes.Status409Conflict, StalePackageFile),
        StudioWriteRefusal.FileMissing => StudioEndpoints.NotFound(NoPackageFile),
        _ => otherwise ?? StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft),
    };
}

/// <summary>The package files of a Studio folder, sorted by file.</summary>
internal sealed record StudioPackageFileListResponse(IReadOnlyList<StudioPackageFileItem> Items);

/// <summary>One package file in the list: its package, its path inside the package and the SHA-256 of its bytes as lower-case hex.</summary>
internal sealed record StudioPackageFileItem(string Package, string Path, string Version);

/// <summary>One package file with its text, read as UTF-8, and its version.</summary>
internal sealed record StudioPackageFileResponse(string Package, string Path, string Version, string Content);
