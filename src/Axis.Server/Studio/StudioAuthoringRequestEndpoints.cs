using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Profiles;
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
/// (400), which is <c>{ text, autoAccept? }</c> with a <c>text</c> of up to 4000 characters. Just before the insert the quota is
/// counted again under a transaction lock, so two sends at once cannot both pass it. The request and its audit record commit
/// together. <c>GET .../authoring-requests</c> lists the newest 100 requests of the application and <c>GET .../authoring-requests/{id}</c>
/// reads one with its steps. The session and role checks and the application come first, and then an <c>id</c> that is not a UUID of this
/// application answers 404. <c>POST .../{id}/approve</c>, <c>.../change-plan</c> and <c>.../drop</c> run those checks, then answer 403
/// to anyone but the request's author, for <c>change-plan</c> check its body <c>{ comment }</c> of 1 to 2000 characters (415, 400), and
/// last check the state under a row lock (409). They answer 200 with the request. Problem titles never contain text from the request.
/// </summary>
internal static class StudioAuthoringRequestEndpoints
{
    private const int MaxText = 4000;

    private const int MaxComment = 2000;

    private const int MaxListed = 100;

    // The pg_advisory_xact_lock key that serializes the count and the insert of the sends in one tenant database.
    private const long SendLockKey = 0x4158_4953_4155_5448; // "AXISAUTH" in ASCII.

    private const string NotTheAuthor = "Only the author of the request may do this.";

    private const string Columns =
        "id, author, text, state, plan, round, tokens, auto_accept, sent_at, approved_at, finished_at";

    private enum Action
    {
        Approve,
        ChangePlan,
        Drop,
    }

    public static void MapStudioAuthoringRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests", SendAsync);
        endpoints.MapGet("/api/studio/apps/{application}/authoring-requests", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/authoring-requests/{id}", GetAsync);
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/approve",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.Approve, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/change-plan",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.ChangePlan, application, id, httpContext, options, database));
        endpoints.MapPost("/api/studio/apps/{application}/authoring-requests/{id}/drop",
            (string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database) =>
                ChangeAsync(Action.Drop, application, id, httpContext, options, database));
    }

    private static async Task<IResult> SendAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        TenantDatabase database,
        TenantQuotas quotas,
        ITenantContextAccessor tenants,
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
            return StudioEndpoints.NotFound("No Studio folder holds an application with this name.");
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

        await AppendAuditAsync(transaction, user.Id, AuditActions.AuthoringRequestSent, applicationId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = await ReadAsync(connection, id, applicationId, application, steps: false, user.DisplayName, cancellationToken);
        return Results.Accepted(Location(application, id), response);
    }

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
        if (!Guid.TryParseExact(id, "D", out var requestId) || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
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
        if (action == Action.ChangePlan)
        {
            if (!HasJsonBody(httpContext.Request))
            {
                return UnsupportedBody();
            }

            if (await ReadBodyAsync(httpContext.Request, cancellationToken) is not { } body || !TryReadComment(body, out comment))
            {
                return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid plan comment.");
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? state;
        await using (var lockRow = new NpgsqlCommand(
            "SELECT state FROM axis.authoring_requests WHERE id = @id FOR UPDATE", connection, transaction))
        {
            lockRow.Parameters.AddWithValue("id", requestId);
            state = await lockRow.ExecuteScalarAsync(cancellationToken) as string;
        }

        var allowed = action switch
        {
            Action.Drop => state is not (null or "accepted" or "dropped"),
            _ => state == "awaitingApproval",
        };
        if (!allowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The request is not in a state that allows this.");
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
                await using (var step = new NpgsqlCommand(
                    """
                    INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, input_summary, finished_at)
                    SELECT @step, @id, coalesce(max(ordinal), 0) + 1, 'planChange', @comment, now()
                    FROM axis.authoring_request_steps WHERE request_id = @id
                    """,
                    connection,
                    transaction))
                {
                    step.Parameters.AddWithValue("step", Guid.CreateVersion7());
                    step.Parameters.AddWithValue("id", requestId);
                    step.Parameters.AddWithValue("comment", comment!);
                    await step.ExecuteNonQueryAsync(cancellationToken);
                }

                break;
            default:
                await ExecuteAsync(
                    "UPDATE axis.authoring_requests SET state = 'dropped', finished_at = now(), claimed_by = NULL, claimed_until = NULL WHERE id = @id",
                    connection, transaction, requestId, cancellationToken);
                await AppendAuditAsync(transaction, user.Id, AuditActions.AuthoringRequestDropped, applicationId, requestId, cancellationToken);
                break;
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(await ReadAsync(connection, requestId, applicationId, application, steps: false, user.DisplayName, cancellationToken));
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
                SELECT kind, input_summary, outcome, tokens, started_at, finished_at
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
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
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
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetInt32(5),
        reader.GetInt64(6),
        reader.GetBoolean(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));

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
            Id.ToString("D"), application, Author, authorName, Text, State, Plan, Round, Tokens, AutoAccept, SentAt, ApprovedAt, FinishedAt, null);
    }
}

/// <summary>
/// An authoring request as the authoring API shows it. <c>Application</c> is the application name of the route.
/// <c>AuthorName</c> is the name in the author's profile, else the session's name, else null. <c>Steps</c>
/// is null except on the read route, where it holds the steps in order.
/// </summary>
internal sealed record AuthoringRequestResponse(
    string Id,
    string Application,
    string Author,
    string? AuthorName,
    string Text,
    string State,
    string? Plan,
    int Round,
    long Tokens,
    bool AutoAccept,
    DateTimeOffset SentAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? FinishedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AuthoringRequestStepResponse>? Steps);

/// <summary>One step of a request: its kind, the summary of its input, its outcome, its tokens and its times.</summary>
internal sealed record AuthoringRequestStepResponse(
    string Kind,
    string? Summary,
    string? Outcome,
    long Tokens,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>The newest requests of an application, newest first.</summary>
internal sealed record AuthoringRequestListResponse(IReadOnlyList<AuthoringRequestResponse> Items);
