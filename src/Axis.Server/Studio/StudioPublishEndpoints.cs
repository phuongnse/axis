using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data.Profiles;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Studio;
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
/// In a cell, <c>publish</c> checks the tenant's storage quota (403 <c>quota-exceeded</c>) right after the session.
/// In a cell, <c>publish</c> and <c>preview</c> of a folder with a file under <c>packages/</c> answer the quota 403 while the
/// tenant's <c>packageCpuSecondsPerDay</c> is 0, after the manifest check and before the body, see <c>docs/reference/tenant-quotas.md#refusals</c>.
/// <c>POST /api/studio/apps/{application}/releases/{id}/activate</c> stores an <c>activate</c> request that names a release of
/// the application's history in this tenant, with no snapshot, and answers as <c>publish</c> does. It runs the same checks
/// as <c>publish</c>, and then answers 404 for an <c>id</c> that is not a UUID or is not in the history. It ignores its body.
/// <c>POST /api/studio/apps/{application}/test</c> needs the author role only. It stores a <c>test</c> request with target
/// <c>tenant</c>, a snapshot and the scenario names, and answers as <c>publish</c> does. It does not check <c>Studio:Tests</c>.
/// The manifest check (409) comes before the body. A body is optional. It must be <c>application/json</c> (415) and a JSON
/// object with only <c>scenarios</c>, a list of strings (400). No body, no list and an empty list store no names, which
/// means every scenario. The publisher runs it. The status route answers
/// <c>scenarios</c> for a <c>test</c>, an empty list until it ends. For a <c>publish</c> or <c>validate</c> it answers the
/// stored contract scenario results, and null when none were stored.
/// </summary>
internal static class StudioPublishEndpoints
{
    private const int MaxLabel = 100;

    private const int MaxNote = 2000;

    public static void MapStudioPublishEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/studio/apps/{application}/validate", ValidateAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/publish", PublishAsync).WithRateLimit(Partition.Publishing).WithStorageQuota();
        endpoints.MapPost("/api/studio/apps/{application}/preview", PreviewAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/test", TestAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/releases/{id}/activate", ActivateAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapGet("/api/studio/apps/{application}/requests/{id}", GetRequestAsync);
    }

    private static async Task<IResult> ActivateAsync(
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
        return await InsertAsync("activate", application, manifest.Name, applicationId, snapshot: null, releaseId, label: null, note: null, user, database, cancellationToken);
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

        if (StudioPreview.Find(catalog, tenants.Current!.TenantId) is null)
        {
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "This tenant has no preview tenant.");
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: not null })
        {
            return NoManifestIdProblem();
        }

        if (StudioFolder.HasPackageFiles(folder)
            && await PackageQuota.RefusalAsync(httpContext.RequestServices, tenants.Current.TenantId, httpContext.RequestAborted) is { } refused)
        {
            return refused;
        }

        return await StoreAsync("publish", application, folder, label: null, note: null, httpContext, database, writeLock, target: "preview");
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
            return NoManifestIdProblem();
        }

        if (StudioFolder.HasPackageFiles(folder)
            && await PackageQuota.RefusalAsync(
                httpContext.RequestServices, httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current!.TenantId, httpContext.RequestAborted) is { } refused)
        {
            return refused;
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
            return NoManifestIdProblem();
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

    /// <summary>The title of the 409 for a manifest without a valid id.</summary>
    internal const string NoManifestId = "The application manifest has no valid id.";

    /// <summary>The title of the 404 for a request the application does not have.</summary>
    internal const string NoRequest = "The application has no publish request with this id.";

    private static IResult NoManifestIdProblem() =>
        StudioEndpoints.Problem(StatusCodes.Status409Conflict, NoManifestId);

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
            return NoManifestIdProblem();
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

        return await InsertAsync(kind, application, manifest.Name, applicationId, SnapshotJson(files), releaseId: null, label, note, user, database, cancellationToken, target, scenarioNames);
    }

    /// <summary>The JSON a request stores as its snapshot: each file's path, version and text.</summary>
    internal static string SnapshotJson(IReadOnlyList<StudioSnapshotFile> files) =>
        new JsonArray([.. files.Select(file => (JsonNode)new JsonObject
        {
            ["file"] = file.File,
            ["version"] = file.Version,
            ["content"] = file.Content,
        })]).ToJsonString();

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
        var requestedAt = await InsertRequestAsync(
            connection, id, kind, applicationId, target, snapshot, releaseId, label, note, user.Id, scenarioNames, draftId: null, folder: null, client: null, cancellationToken);

        // The name is the one in the user's profile, else the session's, as GET answers it.
        var names = await UserProfiles.FindDisplayNamesAsync(connection, [user.Id], cancellationToken);
        var response = new StudioPublishRequestResponse(
            id.ToString("D"), name ?? application, kind, target, "queued", label, note, user.Id, names.GetValueOrDefault(user.Id) ?? user.DisplayName, requestedAt,
            null, null, releaseId?.ToString("D"), JsonDocument.Parse("[]").RootElement.Clone(),
            kind == "test" ? JsonDocument.Parse("[]").RootElement.Clone() : null);
        return Results.Accepted($"/api/studio/apps/{Uri.EscapeDataString(application)}/requests/{id:D}", response);
    }

    /// <summary>Stores the request as queued and answers when it was requested. The one insert of a publish request.</summary>
    internal static async Task<DateTimeOffset> InsertRequestAsync(
        NpgsqlConnection connection,
        Guid id,
        string kind,
        Guid applicationId,
        string target,
        string? snapshot,
        Guid? releaseId,
        string? label,
        string? note,
        string requestedBy,
        string? scenarioNames,
        Guid? draftId,
        string? folder,
        string? client,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests
                (id, application_id, kind, target, status, snapshot, release_id, label, note, requested_by, requested_at, scenario_names, draft_id, folder, client)
            VALUES (@id, @application, @kind, @target, 'queued', @snapshot, @release, @label, @note, @user, clock_timestamp(), @scenarios, @draft, @folder, @client)
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
        command.Parameters.AddWithValue("user", requestedBy);
        command.Parameters.Add(new NpgsqlParameter("scenarios", NpgsqlDbType.Jsonb) { Value = (object?)scenarioNames ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("draft", NpgsqlDbType.Uuid) { Value = (object?)draftId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("folder", NpgsqlDbType.Text) { Value = (object?)folder ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("client", NpgsqlDbType.Text) { Value = (object?)client ?? DBNull.Value });
        await using var inserted = await command.ExecuteReaderAsync(cancellationToken);
        await inserted.ReadAsync(cancellationToken);
        return inserted.GetFieldValue<DateTimeOffset>(0);
    }

    /// <summary>The request of the application with this id, or null. The one read of a publish request.</summary>
    internal static async Task<StoredPublishRequest?> FindRequestAsync(
        NpgsqlConnection connection, Guid applicationId, Guid requestId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT kind, status, label, note, requested_by, requested_at, started_at, finished_at, release_id, diagnostics::text, target, scenario_results::text, requested_by_name, draft_id
            FROM axis.publish_requests
            WHERE id = @id AND application_id = @application
            """,
            connection);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("application", applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredPublishRequest(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13))
            : null;
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
            return NoRequestProblem();
        }

        var cancellationToken = httpContext.RequestAborted;
        var connection = await database.GetConnectionAsync(cancellationToken);
        return await ReadResponseAsync(connection, applicationId, requestId, manifest.Name ?? application, cancellationToken) is { } response
            ? Results.Ok(response)
            : NoRequestProblem();
    }

    /// <summary>The request of the application as the API shows it, named <paramref name="name"/>, or null. The one way to show a publish request.</summary>
    internal static async Task<StudioPublishRequestResponse?> ReadResponseAsync(
        NpgsqlConnection connection, Guid applicationId, Guid requestId, string name, CancellationToken cancellationToken)
    {
        if (await FindRequestAsync(connection, applicationId, requestId, cancellationToken) is not { } stored)
        {
            return null;
        }

        using var diagnostics = JsonDocument.Parse(stored.Diagnostics ?? "[]");
        using var scenarios = stored.Kind == "test"
            ? JsonDocument.Parse(stored.ScenarioResults ?? "[]")
            : stored.ScenarioResults is null ? null : JsonDocument.Parse(stored.ScenarioResults);
        var response = new StudioPublishRequestResponse(
            requestId.ToString("D"),
            name,
            stored.Kind,
            stored.Target,
            stored.Status,
            stored.Label,
            stored.Note,
            stored.RequestedBy,
            stored.RequestedByName,
            stored.RequestedAt,
            stored.StartedAt,
            stored.FinishedAt,
            stored.ReleaseId?.ToString("D"),
            diagnostics.RootElement.Clone(),
            scenarios?.RootElement.Clone());

        var names = await UserProfiles.FindDisplayNamesAsync(connection, [stored.RequestedBy], cancellationToken);
        return response with { RequestedByName = names.GetValueOrDefault(stored.RequestedBy) ?? stored.RequestedByName };
    }

    private static IResult NoRequestProblem() => StudioEndpoints.NotFound(NoRequest);
}

/// <summary>
/// A row of <c>axis.publish_requests</c> as <see cref="StudioPublishEndpoints.FindRequestAsync"/> reads it, without its snapshot.
/// <c>Diagnostics</c> and <c>ScenarioResults</c> are the stored JSON text, null when none were stored.
/// </summary>
internal sealed record StoredPublishRequest(
    string Kind,
    string Status,
    string? Label,
    string? Note,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    Guid? ReleaseId,
    string? Diagnostics,
    string Target,
    string? ScenarioResults,
    string? RequestedByName,
    Guid? DraftId);

/// <summary>
/// A publish request as the authoring API shows it. <c>Application</c> is the manifest name, <c>Target</c> is
/// <c>tenant</c> or <c>preview</c> and <c>Diagnostics</c> is empty until the request ends. <c>Scenarios</c> holds the stored
/// results of a <c>test</c>, <c>publish</c> or <c>validate</c>, and is null when none were stored. For a <c>test</c> it is
/// empty until the request ends. <c>RequestedByName</c> is the name in the requesting user's profile, else the one stored
/// with the request, else null.
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
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ReleaseId,
    JsonElement Diagnostics,
    JsonElement? Scenarios);
