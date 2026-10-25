using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Microsoft.Net.Http.Headers;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Server.Studio;

/// <summary>
/// The routes that ask for an activation. <c>POST /api/studio/apps/{application}/validate</c>, <c>.../publish</c> and
/// <c>.../preview</c> store a snapshot of the folder as a publish request in the database of the tenant of the host, and answer 202 with the
/// request and a <c>Location</c> for <c>GET /api/studio/apps/{application}/requests/{id}</c>. They never compile or
/// activate: the publisher runs the request. The checks run in the order of the rest of the authoring API: session (401),
/// author role (403), for <c>publish</c> the publisher role (403), then the application (404). A folder whose manifest
/// has no UUID <c>id</c> answers 409. A <c>preview</c> needs the author role only. It stores a <c>publish</c> request with
/// target <c>preview</c>, and answers 409 when the tenant has no served preview tenant, before the manifest check. A <c>publish</c> without a body stores no label and no note. A body must be
/// <c>application/json</c> (415) and a JSON object with only a string <c>label</c> of up to 100 characters and a string
/// <c>note</c> of up to 2000 (400). A <c>validate</c> ignores its body. The status route answers 404 for a request
/// of another application or tenant, because it reads this tenant's database for this application only.
/// <c>POST /api/studio/apps/{application}/releases/{id}/rollback</c> stores a <c>rollback</c> request that names a release of
/// the application's history in this tenant, with no snapshot, and answers as <c>publish</c> does. It runs the same checks
/// as <c>publish</c>, and then answers 404 for an <c>id</c> that is not a UUID or is not in the history. It ignores its body.
/// <c>POST /api/studio/apps/{application}/test</c> needs the author role only. It stores a <c>test</c> request with target
/// <c>tenant</c>, a snapshot and the scenario names, and answers as <c>publish</c> does. It does not check <c>Studio:Tests</c>.
/// The manifest check (409) comes before the body. A body is optional. It must be <c>application/json</c> (415) and a JSON
/// object with only <c>scenarios</c>, a list of strings (400). No body, no list and an empty list store no names, which
/// means every scenario. The publisher runs it. The status route answers
/// <c>scenarios</c> for a <c>test</c>, an empty list until it ends, and null for every other kind.
/// </summary>
internal static class StudioPublishEndpoints
{
    private const int MaxLabel = 100;

    private const int MaxNote = 2000;

    public static void MapStudioPublishEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/studio/apps/{application}/validate", ValidateAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/publish", PublishAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/preview", PreviewAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/test", TestAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/releases/{id}/rollback", RollbackAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapGet("/api/studio/apps/{application}/requests/{id}", GetRequestAsync);
    }

    private static async Task<IResult> RollbackAsync(
        string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, publisher: true);
        if (folder is null)
        {
            return refusal!;
        }

        // A folder without a manifest id has never been activated, so its history is empty and every release is unknown.
        var cancellationToken = httpContext.RequestAborted;
        if (!Guid.TryParseExact(id, "D", out var releaseId)
            || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId } manifest
            || !(await new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken))
                .ListActivationsAsync(applicationId, cancellationToken)).Any(row => row.ReleaseId == releaseId))
        {
            return StudioEndpoints.NotFound("The application has no release with this id in its history.");
        }

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        return await InsertAsync("rollback", application, manifest.Name, applicationId, snapshot: null, releaseId, label: null, note: null, user, database, cancellationToken);
    }

    private static async Task<IResult> ValidateAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        return folder is null
            ? refusal!
            : await StoreAsync("validate", application, folder, label: null, note: null, httpContext, database, writeLock);
    }

    private static async Task<IResult> PreviewAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        TenantDatabase database,
        IStudioWriteLock writeLock,
        ITenantCatalog catalog,
        ITenantContextAccessor tenants)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        return StudioPreview.Find(catalog, tenants.Current!.TenantId) is null
            ? StudioEndpoints.Problem(StatusCodes.Status409Conflict, "This tenant has no preview tenant.")
            : await StoreAsync("publish", application, folder, label: null, note: null, httpContext, database, writeLock, target: "preview");
    }

    private static async Task<IResult> PublishAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, publisher: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: not null })
        {
            return NoManifestId();
        }

        var request = httpContext.Request;
        string? label = null;
        string? note = null;
        if (request.ContentLength != 0 && !(request.ContentLength is null && string.IsNullOrEmpty(request.ContentType)))
        {
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
                || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
            }

            try
            {
                var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                    ?? throw new JsonException();
                if (!TryReadBody(body, out label, out note))
                {
                    return BadBody();
                }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return BadBody();
            }
        }

        return await StoreAsync("publish", application, folder, label, note, httpContext, database, writeLock);
    }

    private static async Task<IResult> TestAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: not null })
        {
            return NoManifestId();
        }

        var request = httpContext.Request;
        string? scenarioNames = null;
        if (request.ContentLength != 0 && !(request.ContentLength is null && string.IsNullOrEmpty(request.ContentType)))
        {
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
                || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
            }

            try
            {
                var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                    ?? throw new JsonException();
                if (!TryReadScenarios(body, out scenarioNames))
                {
                    return BadTestBody();
                }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return BadTestBody();
            }
        }

        return await StoreAsync("test", application, folder, label: null, note: null, httpContext, database, writeLock, scenarioNames: scenarioNames);
    }

    // Only scenarios, a list of strings. An empty list means every scenario, like no list.
    private static bool TryReadScenarios(JsonObject body, out string? scenarioNames)
    {
        scenarioNames = null;
        foreach (var (key, value) in body)
        {
            if (key != "scenarios"
                || value is not JsonArray names
                || names.Any(name => name is not JsonValue json || !json.TryGetValue<string>(out _)))
            {
                return false;
            }

            scenarioNames = names.Count == 0 ? null : names.ToJsonString();
        }

        return true;
    }

    private static IResult BadTestBody() =>
        StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid test request.");

    // Only label and note, each a string within its limit.
    private static bool TryReadBody(JsonObject body, out string? label, out string? note)
    {
        label = null;
        note = null;
        foreach (var (key, value) in body)
        {
            if (value is not JsonValue json || !json.TryGetValue<string>(out var text))
            {
                return false;
            }

            switch (key)
            {
                case "label" when text.Length <= MaxLabel:
                    label = text;
                    break;
                case "note" when text.Length <= MaxNote:
                    note = text;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static IResult BadBody() =>
        StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid publish request.");

    private static IResult NoManifestId() =>
        StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The application manifest has no valid id.");

    // The folder is read under the write lock, so the snapshot is what the author saw after their last finished write.
    private static async Task<IResult> StoreAsync(
        string kind,
        string application,
        string folder,
        string? label,
        string? note,
        HttpContext httpContext,
        TenantDatabase database,
        IStudioWriteLock writeLock,
        string target = "tenant",
        string? scenarioNames = null)
    {
        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId } manifest)
        {
            return NoManifestId();
        }

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        var cancellationToken = httpContext.RequestAborted;
        IReadOnlyList<StudioSnapshotFile> files;
        try
        {
            files = await StudioWrites.SnapshotAsync(writeLock, folder, cancellationToken);
        }
        catch (StudioWriteLockUnavailableException)
        {
            return StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable);
        }

        var snapshot = new JsonArray([.. files.Select(file => (JsonNode)new JsonObject
        {
            ["file"] = file.File,
            ["version"] = file.Version,
            ["content"] = file.Content,
        })]);

        return await InsertAsync(kind, application, manifest.Name, applicationId, snapshot.ToJsonString(), releaseId: null, label, note, user, database, cancellationToken, target, scenarioNames);
    }

    // Stores the request as queued and answers 202 with it and its Location.
    private static async Task<IResult> InsertAsync(
        string kind,
        string application,
        string? name,
        Guid applicationId,
        string? snapshot,
        Guid? releaseId,
        string? label,
        string? note,
        CurrentUserResponse user,
        TenantDatabase database,
        CancellationToken cancellationToken,
        string target = "tenant",
        string? scenarioNames = null)
    {
        var id = Guid.CreateVersion7();
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests
                (id, application_id, kind, target, status, snapshot, release_id, label, note, requested_by, requested_by_name, requested_at, scenario_names)
            VALUES (@id, @application, @kind, @target, 'queued', @snapshot, @release, @label, @note, @user, @name, clock_timestamp(), @scenarios)
            RETURNING requested_at
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("target", target);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlDbType.Jsonb) { Value = (object?)snapshot ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("release", NpgsqlDbType.Uuid) { Value = (object?)releaseId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("label", NpgsqlDbType.Text) { Value = (object?)label ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Text) { Value = (object?)note ?? DBNull.Value });
        command.Parameters.AddWithValue("user", user.Id);
        command.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = (object?)user.DisplayName ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("scenarios", NpgsqlDbType.Jsonb) { Value = (object?)scenarioNames ?? DBNull.Value });
        await using var inserted = await command.ExecuteReaderAsync(cancellationToken);
        await inserted.ReadAsync(cancellationToken);
        var requestedAt = inserted.GetFieldValue<DateTimeOffset>(0);

        var response = new StudioPublishRequestResponse(
            id.ToString("D"), name ?? application, kind, target, "queued", label, note, user.Id, requestedAt,
            null, null, releaseId?.ToString("D"), JsonDocument.Parse("[]").RootElement.Clone(),
            kind == "test" ? JsonDocument.Parse("[]").RootElement.Clone() : null);
        return Results.Accepted($"/api/studio/apps/{Uri.EscapeDataString(application)}/requests/{id:D}", response);
    }

    private static async Task<IResult> GetRequestAsync(
        string application, string id, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        // An id that is not a UUID, a request of another application and a manifest without an id all count as unknown.
        if (!Guid.TryParseExact(id, "D", out var requestId)
            || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId } manifest)
        {
            return NoRequest();
        }

        var cancellationToken = httpContext.RequestAborted;
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT kind, status, label, note, requested_by, requested_at, started_at, finished_at, release_id, diagnostics::text, target, scenario_results::text
            FROM axis.publish_requests
            WHERE id = @id AND application_id = @application
            """,
            connection);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("application", applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return NoRequest();
        }

        using var diagnostics = JsonDocument.Parse(reader.IsDBNull(9) ? "[]" : reader.GetString(9));
        var kind = reader.GetString(0);
        using var scenarios = kind == "test" ? JsonDocument.Parse(reader.IsDBNull(11) ? "[]" : reader.GetString(11)) : null;
        return Results.Ok(new StudioPublishRequestResponse(
            requestId.ToString("D"),
            manifest.Name ?? application,
            kind,
            reader.GetString(10),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8).ToString("D"),
            diagnostics.RootElement.Clone(),
            scenarios?.RootElement.Clone()));
    }

    private static IResult NoRequest() =>
        StudioEndpoints.NotFound("The application has no publish request with this id.");
}

/// <summary>
/// A publish request as the authoring API shows it. <c>Application</c> is the manifest name, <c>Target</c> is
/// <c>tenant</c> or <c>preview</c> and <c>Diagnostics</c> is empty until the request ends. <c>Scenarios</c> is null unless
/// the kind is <c>test</c>. For a <c>test</c> it is empty until the request ends.
/// </summary>
internal sealed record StudioPublishRequestResponse(
    string Id,
    string Application,
    string Kind,
    string Target,
    string Status,
    string? Label,
    string? Note,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ReleaseId,
    JsonElement Diagnostics,
    JsonElement? Scenarios);
