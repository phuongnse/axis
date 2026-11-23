using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Profiles;
using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Studio;
using Axis.Tenancy;
using Microsoft.Net.Http.Headers;
using Npgsql;

namespace Axis.Server.Studio;

/// <summary>
/// The routes of an application's authoring requests, see <c>docs/reference/authoring-requests.md#routes</c>.
/// <c>POST /api/studio/apps/{application}/authoring-requests</c> stores a request in the <c>planning</c> state in the database of
/// the tenant of the host and answers 202 with the request and a <c>Location</c> for <c>GET .../authoring-requests/{id}</c>.
/// The checks run in this order: session (401), author role (403), in a cell the tenant's <c>authoringRequests</c> quota
/// (403 <c>quota-exceeded</c>), the application (404), a manifest with a UUID <c>id</c> (409), the content type (415), then the body
/// (400), which is <c>{ text, autoAccept? }</c> with a <c>text</c> of up to 4000 characters. Then the send gets the agent host's token for
/// the author, see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>, before the transaction and the quota lock, so a bad
/// body never calls the realm: a <c>fapi2</c> tenant or one with no agent client answers 409, a refused grant or a session with no
/// platform token 401, a realm that does not answer 503, and an exchanged token without the author role 403. None of them writes a
/// row. Just before the insert the quota is counted again under a transaction lock, so two sends at once cannot both pass it. The
/// request, its encrypted refresh token and its audit record commit together. <c>GET .../authoring-requests</c> lists the newest 100 requests of the application and <c>GET .../authoring-requests/{id}</c>
/// reads one with its steps. The session and role checks and the application come first, and then an <c>id</c> that is not a UUID of this
/// application answers 404. <c>POST .../{id}/approve</c>, <c>.../change-plan</c>, <c>.../request-changes</c>, <c>.../retry</c> and <c>.../drop</c> run those checks, then answer 403
/// to anyone but the request's author, for <c>change-plan</c> and <c>request-changes</c> check its body <c>{ comment }</c> of 1 to 2000 characters (415, 400), and
/// last check the state under a row lock (409). <c>request-changes</c> also answers 409 for a draft that has an accept queued or running, and it
/// keeps the comment as a <c>changeRequest</c> step, sets the round to 0, clears the agent host's claim and moves the request to <c>working</c>.
/// <c>retry</c> takes no body and answers 409 unless the request is <c>ready</c> with a draft whose newest accept failed with <c>AXC0377</c>, a conflict. It
/// clears the draft, sets the round to 0, clears the agent host's claim, stores a <c>retry</c> step with no comment and moves the request to <c>working</c>.
/// They answer 200 with the request. Problem titles never contain text from the request. The drop erases the stored refresh
/// token, and no answer carries it. <c>POST .../{id}/accept</c> takes no body and runs the same checks, then answers 409 for a request that is not
/// <c>ready</c> or has no draft, and 409 for a draft that already has an accept queued or running. Otherwise it stores an <c>accept</c> publish request
/// for the draft with the client <c>studio</c> and answers 202 with that request and a <c>Location</c> for <c>GET .../requests/{id}</c>. The accept writes the draft as a revision, so over the <c>storage</c> quota it is refused before any of these checks, as a save is, see
/// <c>docs/reference/tenant-quotas.md#refusals</c>. It changes no authoring request:
/// the publisher moves it to <c>accepted</c> when the accept succeeds, see <c>docs/reference/authoring-requests.md#accepting</c>.
/// <c>GET .../{id}/diff?cursor=</c> lets any author read what the request changes, after the session and role checks and the application
/// (404 for an <c>id</c> that is not a request of it). An <c>accepted</c> request answers the changes of the revision its accept made
/// as <c>{ start, tip, resources, cursor }</c> with <c>titles</c> and a null <c>cursor</c>, and 400 for a cursor. Any other request that
/// has a draft answers a page of the draft's diff, read as its author's draft, with the draft diff's 400 and 503. A request with neither
/// answers 404. A layer file's entry also answers its <c>module</c>, the module resource with the layer applied before and after the change,
/// and the marks of the after side. See <c>docs/reference/authoring-requests.md#routes</c>.
/// </summary>
internal static class StudioAuthoringRequestEndpoints
{
    private const int MaxText = 4000;

    private const int MaxComment = 2000;

    private const int MaxListed = 100;

    // The pg_advisory_xact_lock key that serializes the count and the insert of the sends in one tenant database.
    private const long SendLockKey = 0x4158_4953_4155_5448; // "AXISAUTH" in ASCII.

    private const string NotTheAuthor = "Only the author of the request may do this.";

    private const string NoChanges = "The request has no draft and no accept.";

    private const string Columns =
        "id, author, text, title, state, plan::text, round, tokens, auto_accept, sent_at, approved_at, finished_at";

    private enum Action
    {
        Approve,
        ChangePlan,
        RequestChanges,
        Retry,
        Drop,
        Accept,
    }

    public static void MapStudioAuthoringRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests", SendAsync);
        endpoints.MapGet("/api/studio/apps/{application}/authoring-requests", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/authoring-requests/{id}", GetAsync);
        endpoints.MapGet("/api/studio/apps/{application}/authoring-requests/{id}/diff", DiffAsync);
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/approve",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.Approve, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/change-plan",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.ChangePlan, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/request-changes",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.RequestChanges, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/retry",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.Retry, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/drop",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.Drop, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/accept",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.Accept, application, id, httpContext, options, database)).WithStorageQuota();
    }

    private static async Task<IResult> SendAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        TenantDatabase database,
        TenantQuotas quotas,
        ITenantContextAccessor tenants,
        IAuthorTokenExchange exchange,
        SensitiveValues values,
        ILoggerFactory loggers)
    {
        if (await StudioEndpoints.RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        var cancellationToken = httpContext.RequestAborted;

        // Outside a cell, or for a tenant with no limit, Limit answers null and the tenant database is never read.
        var tenantId = tenants.Current?.TenantId;
        var limit = tenantId is null ? null : quotas.Limit(tenantId, QuotaKeys.AuthoringRequests);
        if (limit is { } early)
        {
            var used = await CountMonthAsync(await database.GetConnectionAsync(cancellationToken), null, cancellationToken);
            if (used >= early)
            {
                QuotaRefusal.Log(loggers, tenantId!, QuotaKeys.AuthoringRequests);
                return QuotaRefusal.Problem(QuotaKeys.AuthoringRequests, early, used);
            }
        }

        if (StudioFolder.Find(StudioEndpoints.ForRequest(httpContext, options), application) is not { } folder)
        {
            return StudioEndpoints.NotFound(StudioEndpoints.NoApplication);
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The application manifest has no valid id.");
        }

        if (!HasJsonBody(httpContext.Request))
        {
            return UnsupportedBody();
        }

        var body = await ReadBodyAsync(httpContext.Request, cancellationToken);
        if (body is null || !TryReadSend(body, out var text, out var autoAccept))
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid authoring request.");
        }

        var token = await exchange.ExchangeAsync(httpContext, cancellationToken);
        if (token.Outcome != AuthorTokenOutcome.Exchanged)
        {
            return TokenRefusal(token.Outcome);
        }

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        var id = Guid.CreateVersion7();
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (limit is { } limitNow)
        {
            // The lock lasts to the end of the transaction, so a send that waits for it counts the one that just committed.
            await using (var locking = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({SendLockKey})", connection, transaction))
            {
                await locking.ExecuteNonQueryAsync(cancellationToken);
            }

            var used = await CountMonthAsync(connection, transaction, cancellationToken);
            if (used >= limitNow)
            {
                await transaction.RollbackAsync(cancellationToken);
                QuotaRefusal.Log(loggers, tenantId!, QuotaKeys.AuthoringRequests);
                return QuotaRefusal.Problem(QuotaKeys.AuthoringRequests, limitNow, used);
            }
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.authoring_requests (id, application_id, author, text, state, auto_accept, sent_at)
            VALUES (@id, @application, @author, @text, 'planning', @autoAccept, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("application", applicationId);
            insert.Parameters.AddWithValue("author", user.Id);
            insert.Parameters.AddWithValue("text", text);
            insert.Parameters.AddWithValue("autoAccept", autoAccept);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuthoringRequestTokens.StoreAsync(connection, transaction, values, tenantId!, id, token.RefreshToken!, cancellationToken);
        await AppendAuditAsync(transaction, user.Id, AuditActions.AuthoringRequestSent, applicationId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = await ReadAsync(connection, id, applicationId, application, steps: false, user.DisplayName, cancellationToken);
        return Results.Accepted(Location(application, id), response);
    }

    private static IResult TokenRefusal(AuthorTokenOutcome outcome) => outcome switch
    {
        AuthorTokenOutcome.NotOffered => StudioEndpoints.Problem(
            StatusCodes.Status409Conflict, "This tenant cannot let the agent host act for an author."),
        AuthorTokenOutcome.Refused => StudioEndpoints.Problem(
            StatusCodes.Status401Unauthorized, "Sign in again so the agent host can act for you."),
        AuthorTokenOutcome.NotAuthor => StudioEndpoints.Problem(
            StatusCodes.Status403Forbidden, "Your sign-in no longer holds the author role."),
        _ => StudioEndpoints.Problem(
            StatusCodes.Status503ServiceUnavailable, "The identity provider did not answer. Try again later."),
    };

    private static async Task<IResult> ListAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return Results.Ok(new AuthoringRequestListResponse([]));
        }

        var cancellationToken = httpContext.RequestAborted;
        var connection = await database.GetConnectionAsync(cancellationToken);
        var rows = new List<Row>();
        await using (var command = new NpgsqlCommand(
            $"""
            SELECT {Columns} FROM axis.authoring_requests
            WHERE application_id = @application
            ORDER BY sent_at DESC, id DESC
            LIMIT {MaxListed}
            """,
            connection))
        {
            command.Parameters.AddWithValue("application", applicationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(ReadRow(reader));
            }
        }

        var names = await UserProfiles.FindDisplayNamesAsync(connection, [.. rows.Select(row => row.Author).Distinct(StringComparer.Ordinal)], cancellationToken);
        return Results.Ok(new AuthoringRequestListResponse([.. rows.Select(row => row.ToResponse(application, names.GetValueOrDefault(row.Author)))]));
    }

    private static async Task<IResult> GetAsync(
        string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var cancellationToken = httpContext.RequestAborted;
        if (!Guid.TryParseExact(id, "D", out var requestId) || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return NoRequest();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await ReadAsync(connection, requestId, applicationId, application, steps: true, fallbackName: null, cancellationToken) is { } response
            ? Results.Ok(response)
            : NoRequest();
    }

    private static async Task<IResult> DiffAsync(
        string application,
        string id,
        string? cursor,
        HttpContext httpContext,
        StudioOptions options,
        IStudioWriteLock writeLock,
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
        if (!Guid.TryParseExact(id, "D", out var requestId) || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return NoRequest();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        string? author = null;
        string? state = null;
        Guid? draftId = null;
        await using (var find = new NpgsqlCommand(
            "SELECT author, state, draft_id FROM axis.authoring_requests WHERE id = @id AND application_id = @application", connection))
        {
            find.Parameters.AddWithValue("id", requestId);
            find.Parameters.AddWithValue("application", applicationId);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                author = reader.GetString(0);
                state = reader.GetString(1);
                draftId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            }
        }

        if (author is null)
        {
            return NoRequest();
        }

        if (draftId is not { } draft)
        {
            return StudioEndpoints.NotFound(NoChanges);
        }

        if (state == "accepted")
        {
            return await AcceptedDiffAsync(applicationId, draft, cursor, folder, httpContext, options, connection, packages, modules, cancellationToken);
        }

        var names = await UserProfiles.FindDisplayNamesAsync(connection, [author], cancellationToken);
        var drafts = StudioDrafts.For(
            StudioEndpoints.ForRequest(httpContext, options).RevisionsPath, folder, author, names.GetValueOrDefault(author) ?? author, writeLock);
        if (drafts is null)
        {
            return StudioEndpoints.NotFound(NoChanges);
        }

        var outcome = await drafts.DiffAsync(draft.ToString("D"), cursor, cancellationToken);
        return outcome.Refusal switch
        {
            null => await DraftPageAsync(drafts, draft.ToString("D"), outcome.Value!, packages, modules, cancellationToken),
            StudioWriteRefusal.Unavailable => StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable),
            StudioWriteRefusal.DoesNotApply => StudioEndpoints.Problem(StatusCodes.Status400BadRequest, StudioRevisionEndpoints.ForeignCursor),
            _ => StudioEndpoints.NotFound(NoChanges),
        };
    }

    // A page of the draft's diff. A layer file's entry also answers its module resource, from the draft's start commit and from the
    // page's tip, so the files of both are read only when the page has a layer file.
    private static async Task<IResult> DraftPageAsync(
        StudioDrafts drafts,
        string draft,
        StudioDraftDiff page,
        PackageCatalog packages,
        ModuleCatalog modules,
        CancellationToken cancellationToken)
    {
        if (!page.Resources.Any(change => change.Target is not null))
        {
            return Results.Ok(new AuthoringRequestDraftDiff(page.Start, page.Tip, [.. page.Resources.Select(change => DraftEntry(change, null))], page.Cursor));
        }

        var start = await drafts.FilesAsync(draft, page.Start, cancellationToken);
        var tip = await drafts.FilesAsync(draft, page.Tip, cancellationToken);
        if ((start.Refusal ?? tip.Refusal) is { } refusal)
        {
            return refusal == StudioWriteRefusal.Unavailable
                ? StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable)
                : StudioEndpoints.NotFound(NoChanges);
        }

        var startSide = new StudioModuleSide(start.Value!, packages, modules);
        var tipSide = new StudioModuleSide(tip.Value!, packages, modules);
        return Results.Ok(new AuthoringRequestDraftDiff(
            page.Start,
            page.Tip,
            [.. page.Resources.Select(change => DraftEntry(
                change, change.Target is null ? null : StudioModuleLayers.Change(change.Target, startSide, tipSide, modules)))],
            page.Cursor));

        static AuthoringRequestDraftEntry DraftEntry(StudioRevisionChange change, AuthoringRequestLayerChange? module) =>
            new(change.File, change.Change, change.Id, change.Kind, change.Name, change.Before, change.After, change.Target, module);
    }

    // The changes of the revision the draft's newest succeeded accept made, with the titles of each resource, in one page.
    private static async Task<IResult> AcceptedDiffAsync(
        Guid applicationId,
        Guid draftId,
        string? cursor,
        string folder,
        HttpContext httpContext,
        StudioOptions options,
        NpgsqlConnection connection,
        PackageCatalog packages,
        ModuleCatalog modules,
        CancellationToken cancellationToken)
    {
        if (cursor is not null)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, StudioRevisionEndpoints.ForeignCursor);
        }

        Guid? accept;
        await using (var find = new NpgsqlCommand(
            """
            SELECT id FROM axis.publish_requests
            WHERE application_id = @application AND kind = 'accept' AND draft_id = @draft AND status = 'succeeded'
            ORDER BY finished_at DESC NULLS LAST LIMIT 1
            """,
            connection))
        {
            find.Parameters.AddWithValue("application", applicationId);
            find.Parameters.AddWithValue("draft", draftId);
            accept = await find.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (accept is not { } acceptId || await StudioEndpoints.RevisionsAsync(httpContext, options, folder) is not { } revisions)
        {
            return StudioEndpoints.NotFound(NoChanges);
        }

        var list = revisions.List();
        var request = acceptId.ToString("D");
        var index = -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Request, request, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || index + 1 >= list.Count || revisions.Diff(list[index].Id) is not { } diff)
        {
            return StudioEndpoints.NotFound(NoChanges);
        }

        var tipMain = revisions.Main(list[index].Id);
        var parentMain = revisions.Main(list[index + 1].Id);
        var tip = JsonFiles(tipMain);
        var parent = JsonFiles(parentMain);
        var titles = StudioResourceTitles.Read(StudioFolder.Resources(tip));
        var removedTitles = StudioResourceTitles.Read(StudioFolder.Resources(parent));
        var parentSide = new StudioModuleSide(parentMain?.Files ?? [], packages, modules);
        var tipSide = new StudioModuleSide(tipMain?.Files ?? [], packages, modules);
        var resources = diff.Resources
            .Select(change =>
            {
                var read = change.Change == "removed" ? removedTitles : titles;
                var entryTitles = Guid.TryParseExact(change.Id, "D", out var resourceId) && read.TryGetValue(resourceId, out var found)
                    ? found.Titles
                    : null;
                return new AuthoringRequestDiffEntry(
                    change.File, change.Change, change.Id, change.Kind, change.Name, change.Before, change.After, change.Target,
                    entryTitles is { Count: > 0 } ? entryTitles : null,
                    change.Target is null ? null : StudioModuleLayers.Change(change.Target, parentSide, tipSide, modules));
            })
            .ToList();
        return Results.Ok(new AuthoringRequestRevisionDiff(list[index + 1].Id, list[index].Id, resources, null));
    }

    private static IReadOnlyList<(string File, byte[] Bytes)> JsonFiles(StudioMain? main) =>
        [.. (main?.Files ?? []).Where(file => file.File.EndsWith(".json", StringComparison.Ordinal))];

    // The author's checks come before the body and the state, so a person who may not act learns nothing about either.
    private static async Task<IResult> ChangeAsync(
        Action action, string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var cancellationToken = httpContext.RequestAborted;
        if (!Guid.TryParseExact(id, "D", out var requestId) || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId } manifest)
        {
            return NoRequest();
        }

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        var connection = await database.GetConnectionAsync(cancellationToken);
        string? author;
        await using (var find = new NpgsqlCommand(
            "SELECT author FROM axis.authoring_requests WHERE id = @id AND application_id = @application", connection))
        {
            find.Parameters.AddWithValue("id", requestId);
            find.Parameters.AddWithValue("application", applicationId);
            author = await find.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (author is null)
        {
            return NoRequest();
        }

        if (!string.Equals(author, user.Id, StringComparison.Ordinal))
        {
            return StudioEndpoints.Problem(StatusCodes.Status403Forbidden, NotTheAuthor);
        }

        string? comment = null;
        if (action is Action.ChangePlan or Action.RequestChanges)
        {
            if (!HasJsonBody(httpContext.Request))
            {
                return UnsupportedBody();
            }

            if (await ReadBodyAsync(httpContext.Request, cancellationToken) is not { } body || !TryReadComment(body, out comment))
            {
                return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid comment.");
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? state = null;
        Guid? draftId = null;
        await using (var lockRow = new NpgsqlCommand(
            "SELECT state, draft_id FROM axis.authoring_requests WHERE id = @id FOR UPDATE", connection, transaction))
        {
            lockRow.Parameters.AddWithValue("id", requestId);
            await using var reader = await lockRow.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                state = reader.GetString(0);
                draftId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            }
        }

        var allowed = action switch
        {
            Action.Drop => state is not (null or "accepted" or "dropped"),
            Action.Accept => state == "ready" && draftId is not null,
            Action.RequestChanges => state == "ready",
            Action.Retry => state == "ready" && draftId is { } conflictedDraft
                && await LastAcceptConflictedAsync(connection, transaction, applicationId, conflictedDraft, cancellationToken),
            _ => state == "awaitingApproval",
        };
        if (!allowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The request is not in a state that allows this.");
        }

        if (action == Action.RequestChanges
            && draftId is { } pendingDraft
            && await FindPendingAcceptAsync(connection, transaction, pendingDraft, cancellationToken) is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The draft already has an accept that has not finished.");
        }

        if (action == Action.Accept)
        {
            return await AcceptAsync(application, applicationId, draftId!.Value, name: manifest.Name ?? application, user, connection, transaction, cancellationToken);
        }

        switch (action)
        {
            case Action.Approve:
                await ExecuteAsync(
                    "UPDATE axis.authoring_requests SET state = 'working', approved_at = now() WHERE id = @id",
                    connection, transaction, requestId, cancellationToken);
                await AppendAuditAsync(transaction, user.Id, AuditActions.AuthoringRequestApproved, applicationId, requestId, cancellationToken);
                break;
            case Action.ChangePlan:
                await ExecuteAsync(
                    "UPDATE axis.authoring_requests SET state = 'planning' WHERE id = @id",
                    connection, transaction, requestId, cancellationToken);
                await InsertCommentStepAsync("planChange", connection, transaction, requestId, comment!, cancellationToken);
                break;
            case Action.RequestChanges:
                await ExecuteAsync(
                    "UPDATE axis.authoring_requests SET state = 'working', round = 0, claimed_by = NULL, claimed_until = NULL WHERE id = @id",
                    connection, transaction, requestId, cancellationToken);
                await InsertCommentStepAsync("changeRequest", connection, transaction, requestId, comment!, cancellationToken);
                break;
            case Action.Retry:
                await ExecuteAsync(
                    "UPDATE axis.authoring_requests SET state = 'working', round = 0, draft_id = NULL, claimed_by = NULL, claimed_until = NULL WHERE id = @id",
                    connection, transaction, requestId, cancellationToken);
                await InsertCommentStepAsync("retry", connection, transaction, requestId, comment: null, cancellationToken);
                break;
            default:
                await ExecuteAsync(
                    "UPDATE axis.authoring_requests SET state = 'dropped', finished_at = now(), claimed_by = NULL, claimed_until = NULL, refresh_token = NULL WHERE id = @id",
                    connection, transaction, requestId, cancellationToken);
                await AppendAuditAsync(transaction, user.Id, AuditActions.AuthoringRequestDropped, applicationId, requestId, cancellationToken);
                break;
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(await ReadAsync(connection, requestId, applicationId, application, steps: false, user.DisplayName, cancellationToken));
    }

    // A finished step of the given kind that holds the author's comment, if any, after the request's last step.
    private static async Task InsertCommentStepAsync(
        string kind, NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, string? comment, CancellationToken cancellationToken)
    {
        await using var step = new NpgsqlCommand(
            """
            INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, input_summary, finished_at)
            SELECT @step, @id, coalesce(max(ordinal), 0) + 1, @kind, @comment, now()
            FROM axis.authoring_request_steps WHERE request_id = @id
            """,
            connection,
            transaction);
        step.Parameters.AddWithValue("step", Guid.CreateVersion7());
        step.Parameters.AddWithValue("id", requestId);
        step.Parameters.AddWithValue("kind", kind);
        step.Parameters.AddWithValue("comment", (object?)comment ?? DBNull.Value);
        await step.ExecuteNonQueryAsync(cancellationToken);
    }

    // Queues the accept of the request's draft. It changes no authoring request: the publisher moves the request to accepted when the accept succeeds.
    private static async Task<IResult> AcceptAsync(
        string application,
        Guid applicationId,
        Guid draftId,
        string name,
        CurrentUserResponse user,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await FindPendingAcceptAsync(connection, transaction, draftId, cancellationToken) is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The draft already has an accept that has not finished.");
        }

        var id = Guid.CreateVersion7();
        await StudioPublishEndpoints.InsertRequestAsync(
            connection, id, "accept", applicationId, "tenant", snapshot: null, releaseId: null, label: null, note: null, user.Id, scenarioNames: null, draftId,
            folder: application, client: "studio", package: null, operation: null, input: null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = await StudioPublishEndpoints.ReadResponseAsync(connection, applicationId, id, name, cancellationToken);
        return Results.Accepted($"/api/studio/apps/{Uri.EscapeDataString(application)}/requests/{id:D}", response);
    }

    /// <summary>The accept of the draft that is queued or running, or null. The route and <c>accept_draft</c> both queue only when there is none.</summary>
    internal static async Task<Guid?> FindPendingAcceptAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid draftId, CancellationToken cancellationToken)
    {
        await using var check = new NpgsqlCommand(
            "SELECT id FROM axis.publish_requests WHERE kind = 'accept' AND draft_id = @draft AND status IN ('queued', 'running') LIMIT 1",
            connection,
            transaction);
        check.Parameters.AddWithValue("draft", draftId);
        return await check.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    // True when the newest accept of the draft failed with AXC0377, see docs/reference/authoring-requests.md#routes.
    private static async Task<bool> LastAcceptConflictedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid applicationId, Guid draftId, CancellationToken cancellationToken)
    {
        await using var check = new NpgsqlCommand(
            """
            SELECT status = 'failed' AND diagnostics @> jsonb_build_array(jsonb_build_object('code', @code::text))
            FROM axis.publish_requests
            WHERE application_id = @application AND kind = 'accept' AND draft_id = @draft
            ORDER BY requested_at DESC, id DESC
            LIMIT 1
            """,
            connection,
            transaction);
        check.Parameters.AddWithValue("application", applicationId);
        check.Parameters.AddWithValue("draft", draftId);
        check.Parameters.AddWithValue("code", DiagnosticCodes.AcceptConflict);
        return await check.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task ExecuteAsync(
        string sql, NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // The requests sent since 00:00 UTC on the first day of the month. Dropped and failed requests count too.
    private static async Task<long> CountMonthAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM axis.authoring_requests WHERE sent_at >= date_trunc('month', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'",
            connection,
            transaction);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static Task AppendAuditAsync(
        NpgsqlTransaction transaction, string actor, string action, Guid applicationId, Guid requestId, CancellationToken cancellationToken) =>
        AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(actor, action, applicationId, null, null, null, new JsonObject { ["requestId"] = requestId.ToString("D") }),
            cancellationToken);

    private static async Task<AuthoringRequestResponse?> ReadAsync(
        NpgsqlConnection connection, Guid id, Guid applicationId, string application, bool steps, string? fallbackName, CancellationToken cancellationToken)
    {
        Row row;
        await using (var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM axis.authoring_requests WHERE id = @id AND application_id = @application", connection))
        {
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("application", applicationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            row = ReadRow(reader);
        }

        var items = new List<AuthoringRequestStepResponse>();
        if (steps)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT kind, input_summary, outcome, tokens, started_at, finished_at, checks
                FROM axis.authoring_request_steps WHERE request_id = @id ORDER BY ordinal
                """,
                connection);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new AuthoringRequestStepResponse(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : JsonDocument.Parse(reader.GetString(6)).RootElement.Clone()));
            }
        }

        var names = await UserProfiles.FindDisplayNamesAsync(connection, [row.Author], cancellationToken);
        var response = row.ToResponse(application, names.GetValueOrDefault(row.Author) ?? fallbackName);
        return steps ? response with { Steps = items } : response;
    }

    private static Row ReadRow(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetInt32(6),
        reader.GetInt64(7),
        reader.GetBoolean(8),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11));

    private static bool HasJsonBody(HttpRequest request) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
        && mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);

    private static async Task<JsonObject?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Only text, a non-blank string within the limit, and autoAccept, a boolean.
    private static bool TryReadSend(JsonObject body, out string text, out bool autoAccept)
    {
        text = string.Empty;
        autoAccept = false;
        if (body.Any(pair => pair.Key is not ("text" or "autoAccept")))
        {
            return false;
        }

        if (body["text"] is not JsonValue textValue
            || !textValue.TryGetValue<string>(out var value)
            || string.IsNullOrWhiteSpace(value)
            || value.Length > MaxText)
        {
            return false;
        }

        text = value;
        if (body.ContainsKey("autoAccept"))
        {
            return body["autoAccept"] is JsonValue flag && flag.TryGetValue(out autoAccept);
        }

        return true;
    }

    // Only comment, a string of 1 to 2000 characters.
    private static bool TryReadComment(JsonObject body, out string? comment)
    {
        comment = null;
        if (body.Count != 1
            || body["comment"] is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || text.Length is 0 or > MaxComment)
        {
            return false;
        }

        comment = text;
        return true;
    }

    private static string Location(string application, Guid id) =>
        $"/api/studio/apps/{Uri.EscapeDataString(application)}/authoring-requests/{id:D}";

    private static IResult UnsupportedBody() =>
        StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");

    private static IResult NoRequest() =>
        StudioEndpoints.NotFound("The application has no authoring request with this id.");

    private sealed record Row(
        Guid Id,
        string Author,
        string Text,
        string? Title,
        string State,
        string? Plan,
        int Round,
        long Tokens,
        bool AutoAccept,
        DateTimeOffset SentAt,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset? FinishedAt)
    {
        public AuthoringRequestResponse ToResponse(string application, string? authorName) => new(
            Id.ToString("D"), application, Author, authorName, Text, Title, State, Plan is null ? null : JsonDocument.Parse(Plan).RootElement.Clone(), Round, Tokens, AutoAccept, SentAt, ApprovedAt, FinishedAt, null);
    }
}

/// <summary>
/// An authoring request as the authoring API shows it. <c>Application</c> is the application name of the route.
/// <c>AuthorName</c> is the name in the author's profile, else the session's name, else null. <c>Title</c> is the title AI gave the request with its
/// plan, null before it, and <c>Plan</c> is the plan's object of <c>docs/reference/authoring-requests.md#states</c>, null before it. <c>Steps</c>
/// is null except on the read route, where it holds the steps in order.
/// </summary>
internal sealed record AuthoringRequestResponse(
    string Id,
    string Application,
    string Author,
    string? AuthorName,
    string Text,
    string? Title,
    string State,
    JsonElement? Plan,
    int Round,
    long Tokens,
    bool AutoAccept,
    DateTimeOffset SentAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? FinishedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AuthoringRequestStepResponse>? Steps);

/// <summary>One step of a request: its kind, the summary of its input, its outcome, its tokens, its times and the checks of a <c>check</c> or <c>review</c> step, null on any other.</summary>
internal sealed record AuthoringRequestStepResponse(
    string Kind,
    string? Summary,
    string? Outcome,
    long Tokens,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    JsonElement? Checks);

/// <summary>The newest requests of an application, newest first.</summary>
internal sealed record AuthoringRequestListResponse(IReadOnlyList<AuthoringRequestResponse> Items);

/// <summary>One file an accepted request's revision changed, as <see cref="StudioRevisionChange"/> gives it, with the resource's label in each locale, or null when it has none, and its <c>module</c> when it is a layer file.</summary>
internal sealed record AuthoringRequestDiffEntry(
    string File,
    string Change,
    string? Id,
    string? Kind,
    string? Name,
    string? Before,
    string? After,
    string? Target,
    IReadOnlyDictionary<string, string>? Titles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AuthoringRequestLayerChange? Module);

/// <summary>One file a draft's diff changed, as <see cref="StudioRevisionChange"/> gives it, and its <c>module</c> when it is a layer file. It has no titles, as a ready request's diff has none.</summary>
internal sealed record AuthoringRequestDraftEntry(
    string File,
    string Change,
    string? Id,
    string? Kind,
    string? Name,
    string? Before,
    string? After,
    string? Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AuthoringRequestLayerChange? Module);

/// <summary>A page of a draft's diff, as <see cref="StudioDraftDiff"/> gives it, with the entries of <see cref="AuthoringRequestDraftEntry"/>.</summary>
internal sealed record AuthoringRequestDraftDiff(string Start, string Tip, IReadOnlyList<AuthoringRequestDraftEntry> Resources, string? Cursor);

/// <summary>
/// A layer file's module resource in a request's diff: its id, kind, name and file, its JSON text before and after the change
/// with the layer applied on each side, and the marks and removed elements of the after side. A side is null where the layer's
/// target names no module resource. See <c>docs/reference/authoring-requests.md#routes</c>.
/// </summary>
internal sealed record AuthoringRequestLayerChange(
    string Id,
    string Kind,
    string? Name,
    string File,
    string? Before,
    string? After,
    IReadOnlyList<StudioMark> Marks,
    IReadOnlyList<StudioRemovedElement> Removed);

/// <summary>The changes of the revision an accepted request made: the commit before it as <c>Start</c>, the revision as <c>Tip</c>, and a null <c>Cursor</c>.</summary>
internal sealed record AuthoringRequestRevisionDiff(string Start, string Tip, IReadOnlyList<AuthoringRequestDiffEntry> Resources, string? Cursor);
