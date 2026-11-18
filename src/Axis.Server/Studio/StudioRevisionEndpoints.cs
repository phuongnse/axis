using System.Text;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Studio;
using Npgsql;

namespace Axis.Server.Studio;

/// <summary>
/// The routes of an application's history. <c>GET /api/studio/apps/{application}/revisions</c> lists the folder's revisions
/// as <c>{ items: [{ id, summary, author, userId, client, request, time }], page, pageSize, totalCount, start }</c>: one page,
/// newest first unless <c>sort</c> says otherwise, for the record API's <c>page</c>, <c>pageSize</c>, <c>sort</c>,
/// <c>search</c> and <c>filter</c> over the columns <c>summary</c>, <c>author</c>, <c>time</c> and <c>client</c>, and 400
/// for an invalid parameter. <c>start</c> is the id of the start of history, or null for none. See
/// <c>docs/reference/authoring-revisions.md#list</c>.
/// <c>GET .../revisions/{revision}</c> answers one revision with its files as <c>{ revision, resources: [{ file, change,
/// id, kind, name, before, after }] }</c>. <c>POST .../revisions/{revision}/undo</c> puts back the files the revision changed,
/// records the revert as a new revision and answers <c>{ id, diagnostics }</c>. A revision is named by its full commit id.
/// <c>GET /api/studio/apps/{application}/drafts/{draft}/diff?cursor=</c> answers one page of the diff of the signed-in
/// user's draft as <c>{ start, tip, resources, cursor }</c>, 404 for an unknown draft or one that is someone else's, 400
/// for a cursor the draft did not give and 503 when the write lock cannot be taken. See
/// <c>docs/reference/revisions-and-drafts.md#drafts</c>.
/// <c>GET /api/studio/apps/{application}/releases/live/diff?cursor=</c> answers one page of the files that differ between the live
/// release and the working folder as <c>{ release, resources, cursor }</c>, each file with the revisions made since the live
/// release was first activated that changed it, as <c>titles</c> and <c>by</c>. It answers 404 with no live release and with no
/// revision history, 400 for a cursor it did not give and 503 when the write lock cannot be taken. See
/// <c>docs/reference/authoring-revisions.md#live-diff</c>.
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

    internal const string NoDraft = "The application has no draft with this id.";

    internal const string ForeignCursor = "The cursor is not one this draft gave.";

    internal const string NoLiveRelease = "The application has no live release.";

    internal const string NoHistory = "The application has no revision history.";

    public static void MapStudioRevisionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/revisions", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/revisions/{revision}", GetAsync);
        endpoints.MapPost("/api/studio/apps/{application}/revisions/{revision}/undo", UndoAsync).WithStorageQuota();
        endpoints.MapGet("/api/studio/apps/{application}/drafts/{draft}/diff", DraftDiffAsync);
        endpoints.MapGet("/api/studio/apps/{application}/releases/live/diff", LiveDiffAsync);
    }

    // The order of checks is that of docs/reference/authoring-revisions.md#order-of-checks.
    private static async Task<IResult> LiveDiffAsync(
        string application, string? cursor, HttpContext httpContext, StudioOptions options, IStudioWriteLock writeLock, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var ct = httpContext.RequestAborted;
        if (StudioFolder.List(folder).FirstOrDefault(file => file.File == "application.json")?.Id is not { } applicationId)
        {
            return StudioEndpoints.NotFound(NoLiveRelease);
        }

        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(ct));
        if (await store.FindByApplicationIdAsync(applicationId, ct) is not { } active
            || await store.GetReleaseAsync(active.ReleaseId, ct) is not { } release)
        {
            return StudioEndpoints.NotFound(NoLiveRelease);
        }

        // Activations are newest first. A release can be in the history more than once, always with one number, and its
        // first activation is its publish.
        var activations = (await store.ListActivationsAsync(applicationId, ct)).Where(row => row.ReleaseId == active.ReleaseId).ToList();
        if (activations.Count == 0)
        {
            return StudioEndpoints.NotFound(NoLiveRelease);
        }

        if (await StudioEndpoints.RevisionsAsync(httpContext, options, folder) is not { } revisions)
        {
            return StudioEndpoints.NotFound(NoHistory);
        }

        StudioMain main;
        var from = 0;
        if (await StudioWrites.TryTakeAsync(writeLock, folder, ct) is not { } held)
        {
            return StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable);
        }

        await using (held)
        {
            revisions.RecordOutside();
            string? at = null;
            if (cursor is not null)
            {
                if (StudioDiffPages.ReadCursor(cursor) is not var (sha, index))
                {
                    return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, ForeignCursor);
                }

                at = sha;
                from = index;
            }

            if (revisions.Main(at) is not { } found)
            {
                return cursor is null ? StudioEndpoints.NotFound(NoHistory) : StudioEndpoints.Problem(StatusCodes.Status400BadRequest, ForeignCursor);
            }

            main = found;
        }

        var releaseTexts = release.Resources.ToDictionary(resource => resource.Path, resource => resource.Content, StringComparer.Ordinal);
        var tipTexts = main.Files.ToDictionary(file => file.File, file => Encoding.UTF8.GetString(file.Bytes), StringComparer.Ordinal);
        var changes = StudioReleaseChanges.Compare(
            [.. tipTexts.Select(file => new ResourceContent(file.Key, file.Value))],
            [.. release.Resources.Select(resource => new ResourceContent(resource.Path, resource.Content))]);
        var titles = StudioResourceTitles.Read(StudioFolder.Resources([.. main.Files.Where(file => file.File.EndsWith(".json", StringComparison.Ordinal))]));

        // Git keeps commit times to the second and an activation has microseconds, so the activation's own second counts.
        var activatedAt = activations[^1].ActivatedAt.ToUniversalTime();
        var boundary = new DateTimeOffset(activatedAt.Ticks - activatedAt.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var since = main.History.Where(item => item.Revision.Time >= boundary).ToList();

        var entries = new List<(StudioRevisionChange Entry, IReadOnlyDictionary<string, string>? Titles, List<StudioRevision> By)>();
        foreach (var change in changes)
        {
            var entry = StudioRevisions.Entry(
                change.Path, change.Change, releaseTexts.GetValueOrDefault(change.Path), tipTexts.GetValueOrDefault(change.Path));
            var entryTitles = change.Change == "removed"
                ? change.Titles
                : Guid.TryParseExact(entry.Id, "D", out var id) && titles.TryGetValue(id, out var read) ? read.Titles : null;
            entries.Add((entry, entryTitles is { Count: > 0 } ? entryTitles : null, [.. since.Where(item => item.Paths.Contains(change.Path)).Select(item => item.Revision)]));
        }

        var requests = await FindAuthoringRequestsAsync(
            database, applicationId, [.. entries.SelectMany(item => item.By).Select(revision => revision.Request).OfType<string>().Distinct()], ct);
        var live = entries
            .Select(item => new StudioLiveDiffEntry(
                item.Entry.File, item.Entry.Change, item.Entry.Id, item.Entry.Kind, item.Entry.Name, item.Entry.Before, item.Entry.After, item.Entry.Target,
                item.Titles,
                [.. item.By.Select(revision => new StudioLiveRevision(
                    revision.Id, revision.Summary, revision.Author, revision.UserId, revision.Client, revision.Request, revision.Time,
                    revision.Request is { } request ? requests.GetValueOrDefault(request) : null))]))
            .ToList();
        if (cursor is not null && from >= live.Count)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, ForeignCursor);
        }

        var (page, after) = StudioDiffPages.Page(live, from, main.Sha);
        return Results.Ok(new StudioLiveDiff(activations[0].Number, page, after));
    }

    // The AI request of each accept publish request, found through the accept's draft. A request with no match has none.
    private static async Task<IReadOnlyDictionary<string, Guid>> FindAuthoringRequestsAsync(
        TenantDatabase database, Guid applicationId, IReadOnlyList<string> publishRequests, CancellationToken ct)
    {
        var ids = publishRequests.Select(id => Guid.TryParseExact(id, "D", out var parsed) ? parsed : (Guid?)null).OfType<Guid>().ToArray();
        var found = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return found;
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT p.id, a.id
            FROM axis.publish_requests p
            JOIN axis.authoring_requests a ON a.draft_id = p.draft_id AND a.application_id = p.application_id
            WHERE p.application_id = @application AND p.kind = 'accept' AND p.id = ANY(@ids)
            """,
            await database.GetConnectionAsync(ct));
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            found[reader.GetGuid(0).ToString("D")] = reader.GetGuid(1);
        }

        return found;
    }

    private static async Task<IResult> DraftDiffAsync(
        string application, string draft, string? cursor, HttpContext httpContext, StudioOptions options, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        var drafts = StudioDrafts.For(StudioEndpoints.ForRequest(httpContext, options).RevisionsPath, folder, user.Id, user.DisplayName, writeLock);
        if (drafts is null)
        {
            return StudioEndpoints.NotFound(NoDraft);
        }

        var outcome = await drafts.DiffAsync(draft, cursor, httpContext.RequestAborted);
        return outcome.Refusal switch
        {
            null => Results.Ok(outcome.Value),
            StudioWriteRefusal.Unavailable => StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable),
            StudioWriteRefusal.DoesNotApply => StudioEndpoints.Problem(StatusCodes.Status400BadRequest, ForeignCursor),
            _ => StudioEndpoints.NotFound(NoDraft),
        };
    }

    // A time filters by its day in UTC, as a date column does, and sorts by its instant.
    private static readonly IReadOnlyList<ListColumn<StudioRevision>> _listColumns =
    [
        new("summary", ListColumnKind.Text, revision => revision.Summary, Searchable: true),
        new("author", ListColumnKind.Text, revision => revision.Author, Searchable: true),
        new("time", ListColumnKind.Date, revision => DateOnly.FromDateTime(revision.Time.UtcDateTime), Order: revision => revision.Time),
        new("client", ListColumnKind.Text, revision => revision.Client),
    ];

    private static async Task<IResult> ListAsync(string application, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<StudioRevision>.Read(httpContext.Request, _listColumns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var revisions = await StudioEndpoints.RevisionsAsync(httpContext, options, folder);
        var all = revisions?.List() ?? [];
        var page = query.Apply(all);

        // The start of history is the oldest revision, which the list holds last.
        return Results.Ok(new StudioRevisionListResponse(page.Items, page.Page, page.PageSize, page.TotalCount, all.Count > 0 ? all[^1].Id : null));
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

/// <summary>One page of the revisions of an application folder, with the id of the start of history.</summary>
internal sealed record StudioRevisionListResponse(IReadOnlyList<StudioRevision> Items, int Page, int PageSize, long TotalCount, string? Start);

/// <summary>The answer to an undo: the id of the revision it recorded and the folder's compile diagnostics.</summary>
internal sealed record StudioUndoResponse(string? Id, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>A revision of a live diff's <c>by</c>: the revision list's fields and the AI request whose accept made it, or null.</summary>
internal sealed record StudioLiveRevision(
    string Id, string Summary, string Author, string? UserId, string? Client, string? Request, DateTimeOffset Time, Guid? AuthoringRequest);

/// <summary>A file that differs from the live release: the revision diff's fields, its titles by locale or null, and the revisions made since the release that changed it, newest first.</summary>
internal sealed record StudioLiveDiffEntry(
    string File,
    string Change,
    string? Id,
    string? Kind,
    string? Name,
    string? Before,
    string? After,
    string? Target,
    IReadOnlyDictionary<string, string>? Titles,
    IReadOnlyList<StudioLiveRevision> By);

/// <summary>One page of the live diff: the live release's number, the files in this page sorted by file and the cursor of the next page, null on the last page.</summary>
internal sealed record StudioLiveDiff(int Release, IReadOnlyList<StudioLiveDiffEntry> Resources, string? Cursor);
