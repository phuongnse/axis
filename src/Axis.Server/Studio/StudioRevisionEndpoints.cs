using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Server.Tenancy;

namespace Axis.Server.Studio;

/// <summary>
/// The routes of an application's history. <c>GET /api/studio/apps/{application}/revisions</c> lists the folder's revisions,
/// newest first, as <c>{ items: [{ id, summary, author, userId, client, request, time }] }</c>.
/// <c>GET .../revisions/{revision}</c> answers one revision with its files as <c>{ revision, resources: [{ file, change,
/// id, kind, name, before, after }] }</c>. <c>POST .../revisions/{revision}/undo</c> puts back the files the revision changed,
/// records the revert as a new revision and answers <c>{ id, diagnostics }</c>. A revision is named by its full commit id.
/// Each route checks the session (401), the author role (403) and the application (404). A server without
/// <c>Studio:RevisionsPath</c>, or a folder with no history yet, lists no revisions and answers 404 for a diff and an undo, as
/// it does for an id that is not 40 hex characters or not a revision. The undo then answers 503 when the write lock cannot be taken,
/// and 409 for the start of history and for a revision whose files a later revision changed, with those revisions in
/// the <c>revisions</c> extension of the problem. In a cell the undo checks the tenant's storage quota right after the session. See
/// <c>docs/reference/revisions-and-drafts.md#undo</c>.
/// </summary>
internal static class StudioRevisionEndpoints
{
    private const string NoRevision = "The application has no revision with this id.";

    public static void MapStudioRevisionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/revisions", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/revisions/{revision}", GetAsync);
        endpoints.MapPost("/api/studio/apps/{application}/revisions/{revision}/undo", UndoAsync).WithStorageQuota();
    }

    private static async Task<IResult> ListAsync(string application, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var revisions = await StudioEndpoints.RevisionsAsync(httpContext, options, folder);
        return Results.Ok(new StudioRevisionListResponse(revisions?.List() ?? []));
    }

    private static async Task<IResult> GetAsync(string application, string revision, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var diff = IsCommitId(revision) ? (await StudioEndpoints.RevisionsAsync(httpContext, options, folder))?.Diff(revision) : null;
        return diff is null ? StudioEndpoints.NotFound(NoRevision) : Results.Ok(diff);
    }

    private static async Task<IResult> UndoAsync(
        string application,
        string revision,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (!IsCommitId(revision)
            || await StudioEndpoints.RevisionsAsync(httpContext, options, folder) is not { } revisions
            || !revisions.Has(revision))
        {
            return StudioEndpoints.NotFound(NoRevision);
        }

        var outcome = await StudioWrites.UndoAsync(writeLock, revisions, folder, revision, httpContext.RequestAborted);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable);
            case StudioWriteRefusal.NotFound:
                return StudioEndpoints.NotFound(NoRevision);
            case StudioWriteRefusal.UnsafePath:
                return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "A file of the revision is not a safe path in the folder.");
            case StudioWriteRefusal.Blocked when outcome.IsStart:
                return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The start of history cannot be undone.");
            case StudioWriteRefusal.Blocked:
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A later revision changed the same files. Undo it first.",
                    extensions: new Dictionary<string, object?> { ["revisions"] = outcome.Blockers });
        }

        return Results.Ok(new StudioUndoResponse(outcome.Id, StudioDiagnostics.Compile(folder, packages, modules)));
    }

    // A revision is named by its full commit id.
    private static bool IsCommitId(string value) => value.Length == 40 && value.All(char.IsAsciiHexDigit);
}

/// <summary>The revisions of an application folder, newest first.</summary>
internal sealed record StudioRevisionListResponse(IReadOnlyList<StudioRevision> Items);

/// <summary>The answer to an undo: the id of the revision it recorded and the folder's compile diagnostics.</summary>
internal sealed record StudioUndoResponse(string? Id, IReadOnlyList<StudioDiagnostic> Diagnostics);
