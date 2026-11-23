using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
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
/// <c>POST /api/studio/apps/{application}/drafts/{draft}/preview</c> stores the same <c>publish</c> request with target <c>preview</c>
/// from the files of the author's own draft, with its id as <c>draft_id</c>, see <c>docs/reference/authoring-requests.md#routes</c>. The checks run in
/// order: session (401), author role (403), application (404), the caller's own draft (404, or 503 when the write lock is busy), the preview tenant (409), the manifest
/// check (409), then the package quota when the draft has a file under <c>packages/</c>.
/// <c>POST /api/studio/apps/{application}/releases/{id}/activate</c> stores an <c>activate</c> request that names a release of
/// the application's history in this tenant, with no snapshot, and answers as <c>publish</c> does. It runs the same checks
/// as <c>publish</c>, and then answers 404 for an <c>id</c> that is not a UUID or is not in the history. It ignores its body.
/// <c>POST /api/studio/apps/{application}/test</c> needs the author role only. It stores a <c>test</c> request with target
/// <c>tenant</c>, a snapshot and the scenario names, and answers as <c>publish</c> does. It does not check <c>Studio:Tests</c>.
/// The manifest check (409) comes before the body. A body is optional. It must be <c>application/json</c> (415) and a JSON
/// object with only <c>scenarios</c>, a list of strings, or only <c>package</c>, a string (400). No body, no list and an empty list store no names, which
/// means every scenario. A body with <c>package</c> stores a package test of that package's working files instead. It needs the developer role
/// and the tenant's application packages switch (403), checked once the body is read, as only the body says it names a package, and then a package with
/// a file in the folder (404). It checks no quota. The publisher runs it. <c>POST /api/studio/apps/{application}/build</c> stores a <c>build</c> request of one
/// package of the working folder, with the snapshot, and answers as <c>publish</c> does. It needs the developer role and the tenant's
/// application packages switch, checked after the author role (403), and then the manifest check (409). The body must be
/// <c>application/json</c> (415) and exactly <c>{ "package": "&lt;id&gt;" }</c> with a string (400). A package with no file in the folder answers 404.
/// It checks no quota, as a <c>test</c> does not. The publisher sends the package to the host and releases nothing. The status route answers
/// <c>scenarios</c> for a scenario <c>test</c>, an empty list until it ends. For a <c>publish</c> or <c>validate</c> it answers the
/// stored contract scenario results, and null when none were stored, and for a package <c>test</c> it answers null. It answers <c>package</c>
/// for a <c>build</c> and a package <c>test</c>, <c>packageTests</c> for a package <c>test</c>, and <c>output</c> for a <c>build</c>, a <c>run</c> and a package <c>test</c>.
/// <c>POST /api/studio/apps/{application}/run</c> stores a <c>run</c> request of one operation of one package of the working folder, with the snapshot,
/// and answers as <c>publish</c> does. It runs the checks of <c>build</c>: the developer role and the tenant's application packages switch (403), the manifest check (409), then
/// the body, which must be <c>application/json</c> (415) and exactly <c>{ "package", "operation", "input" }</c> with a string package, a string operation and
/// an object input (400). A package with no file in the folder answers 404. It checks no quota and leaves the operation and the input to the publisher. The status route
/// answers <c>run</c> for a <c>run</c>, null until it ends, and null for every other kind.
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
        endpoints.MapPost("/api/studio/apps/{application}/drafts/{draft}/preview", PreviewDraftAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/test", TestAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/build", BuildAsync).WithRateLimit(Partition.Publishing);
        endpoints.MapPost("/api/studio/apps/{application}/run", RunAsync).WithRateLimit(Partition.Publishing);
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

    // The order of checks is that of docs/reference/authoring-requests.md#routes.
    private static async Task<IResult> PreviewDraftAsync(
        string application,
        string draft,
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

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        var drafts = StudioDrafts.For(StudioEndpoints.ForRequest(httpContext, options).RevisionsPath, folder, user.Id, user.DisplayName, writeLock);
        if (drafts is null)
        {
            return StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft);
        }

        var cancellationToken = httpContext.RequestAborted;
        var outcome = await drafts.FilesAsync(draft, cancellationToken);
        if (outcome.Refusal is { } refused)
        {
            return refused == StudioWriteRefusal.Unavailable
                ? StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable)
                : StudioEndpoints.NotFound(StudioRevisionEndpoints.NoDraft);
        }

        if (StudioPreview.Find(catalog, tenants.Current!.TenantId) is null)
        {
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "This tenant has no preview tenant.");
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId } manifest)
        {
            return NoManifestIdProblem();
        }

        var files = outcome.Value!;
        if (files.Any(file => file.File.StartsWith(ApplicationLoader.PackagesFolderPrefix, StringComparison.Ordinal))
            && await PackageQuota.RefusalAsync(httpContext.RequestServices, tenants.Current.TenantId, cancellationToken) is { } quotaRefusal)
        {
            return quotaRefusal;
        }

        return await InsertAsync(
            "publish", application, manifest.Name, applicationId, SnapshotJson(StudioFolder.Snapshot(files)), releaseId: null, label: null, note: null, user, database,
            cancellationToken, target: "preview", draftId: Guid.Parse(draft));
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
        string? package = null;
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
                if (!TryReadTestBody(body, out scenarioNames, out package))
                {
                    return BadTestBody();
                }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return BadTestBody();
            }
        }

        // Only the body says whether this is a package test, so the developer role and the switch are checked once it is read.
        if (package is not null)
        {
            if (await StudioEndpoints.RefuseAsync(httpContext, options, developer: true) is { } denied)
            {
                return denied;
            }

            if (!HasPackage(folder, package))
            {
                return StudioEndpoints.NotFound(StudioPackageFileEndpoints.NoPackage);
            }
        }

        return await StoreAsync("test", application, folder, label: null, note: null, httpContext, database, writeLock, scenarioNames: scenarioNames, package: package);
    }

    // Builds one package of the working folder without a publish, see docs/reference/studio-packages.md#the-authoring-api. The checks
    // follow the authoring API's order. The body is exactly { package }, and the package must have a file in the working folder.
    private static async Task<IResult> BuildAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, developer: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: not null })
        {
            return NoManifestIdProblem();
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        string package;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
            if (!TryReadPackage(body, out package))
            {
                return BadBuildBody();
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return BadBuildBody();
        }

        if (!HasPackage(folder, package))
        {
            return StudioEndpoints.NotFound(StudioPackageFileEndpoints.NoPackage);
        }

        return await StoreAsync("build", application, folder, label: null, note: null, httpContext, database, writeLock, package: package);
    }

    // Runs one operation of one package of the working folder, see docs/reference/studio-packages.md#the-authoring-api. The checks follow
    // BuildAsync's order. The body is exactly { package, operation, input }. The route leaves the operation and the input to the publisher, which
    // ends the run failed when the manifest has no such operation or the input does not fit it.
    private static async Task<IResult> RunAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, developer: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: not null })
        {
            return NoManifestIdProblem();
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        string package;
        string operation;
        JsonObject input;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
            if (!TryReadRunBody(body, out package, out operation, out input))
            {
                return BadRunBody();
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return BadRunBody();
        }

        if (!HasPackage(folder, package))
        {
            return StudioEndpoints.NotFound(StudioPackageFileEndpoints.NoPackage);
        }

        return await StoreAsync(
            "run", application, folder, label: null, note: null, httpContext, database, writeLock, package: package, operation: operation, input: input.ToJsonString());
    }

    // Only package and operation, strings, and input, an object.
    private static bool TryReadRunBody(JsonObject body, out string package, out string operation, out JsonObject input)
    {
        package = "";
        operation = "";
        input = [];
        if (body.Count != 3
            || body["package"] is not JsonValue packageJson || !packageJson.TryGetValue<string>(out var packageText)
            || body["operation"] is not JsonValue operationJson || !operationJson.TryGetValue<string>(out var operationText)
            || body["input"] is not JsonObject inputObject)
        {
            return false;
        }

        package = packageText;
        operation = operationText;
        input = inputObject;
        return true;
    }

    private static IResult BadRunBody() =>
        StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid run request.");

    // A package has a file in the working folder, and its id is a valid package id.
    private static bool HasPackage(string folder, string package) =>
        StudioPackageFiles.File(package, PackageCatalog.ManifestFileName) is not null
        && StudioPackageFiles.List(folder).Any(item => item.Package == package);

    // Only package, a string.
    private static bool TryReadPackage(JsonObject body, out string package)
    {
        package = "";
        if (body.Count != 1 || body["package"] is not JsonValue json || !json.TryGetValue<string>(out var text))
        {
            return false;
        }

        package = text;
        return true;
    }

    private static IResult BadBuildBody() =>
        StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid build request.");

    // Only scenarios, a list of strings, or only package, a string, never both. An empty list means every scenario, like no list.
    private static bool TryReadTestBody(JsonObject body, out string? scenarioNames, out string? package)
    {
        scenarioNames = null;
        package = null;
        if (body.Count > 1)
        {
            return false;
        }

        foreach (var (key, value) in body)
        {
            switch (key)
            {
                case "scenarios" when value is JsonArray names && names.All(name => name is JsonValue json && json.TryGetValue<string>(out _)):
                    scenarioNames = names.Count == 0 ? null : names.ToJsonString();
                    break;
                case "package" when value is JsonValue json && json.TryGetValue<string>(out var text):
                    package = text;
                    break;
                default:
                    return false;
            }
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
        string? scenarioNames = null,
        string? package = null,
        string? operation = null,
        string? input = null)
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

        return await InsertAsync(kind, application, manifest.Name, applicationId, SnapshotJson(files), releaseId: null, label, note, user, database, cancellationToken, target, scenarioNames, package, operation, input);
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
        string? scenarioNames = null,
        string? package = null,
        string? operation = null,
        string? input = null,
        Guid? draftId = null)
    {
        var id = Guid.CreateVersion7();
        var connection = await database.GetConnectionAsync(cancellationToken);
        var requestedAt = await InsertRequestAsync(
            connection, id, kind, applicationId, target, snapshot, releaseId, label, note, user.Id, scenarioNames, draftId, folder: null, client: null, package, operation, input, cancellationToken);

        // The name is the one in the user's profile, else the session's, as GET answers it.
        var names = await UserProfiles.FindDisplayNamesAsync(connection, [user.Id], cancellationToken);
        var response = new StudioPublishRequestResponse(
            id.ToString("D"), name ?? application, kind, target, "queued", label, note, user.Id, names.GetValueOrDefault(user.Id) ?? user.DisplayName, requestedAt,
            null, null, releaseId?.ToString("D"), JsonDocument.Parse("[]").RootElement.Clone(),
            kind == "test" && package is null ? JsonDocument.Parse("[]").RootElement.Clone() : null,
            package,
            kind == "test" && package is not null ? JsonDocument.Parse("[]").RootElement.Clone() : null,
            kind is "build" or "run" || package is not null ? JsonDocument.Parse("[]").RootElement.Clone() : null,
            Run: null);
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
        string? package,
        string? operation,
        string? input,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests
                (id, application_id, kind, target, status, snapshot, release_id, label, note, requested_by, requested_at, scenario_names, draft_id, folder, client, package, operation, input)
            VALUES (@id, @application, @kind, @target, 'queued', @snapshot, @release, @label, @note, @user, clock_timestamp(), @scenarios, @draft, @folder, @client, @package, @operation, @input)
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
        command.Parameters.Add(new NpgsqlParameter("package", NpgsqlDbType.Text) { Value = (object?)package ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("operation", NpgsqlDbType.Text) { Value = (object?)operation ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = (object?)input ?? DBNull.Value });
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
            SELECT kind, status, label, note, requested_by, requested_at, started_at, finished_at, release_id, diagnostics::text, target, scenario_results::text, requested_by_name, draft_id, package, package_tests::text, output::text, run::text
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
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17))
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
        var packageTest = stored.Kind == "test" && stored.Package is not null;
        using var scenarios = packageTest
            ? null
            : stored.Kind == "test"
                ? JsonDocument.Parse(stored.ScenarioResults ?? "[]")
                : stored.ScenarioResults is null ? null : JsonDocument.Parse(stored.ScenarioResults);
        using var packageTests = packageTest ? JsonDocument.Parse(stored.PackageTests ?? "[]") : null;
        using var output = stored.Kind is "build" or "run" || packageTest ? JsonDocument.Parse(stored.Output ?? "[]") : null;
        using var run = stored.Run is null ? null : JsonDocument.Parse(stored.Run);
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
            scenarios?.RootElement.Clone(),
            stored.Package,
            packageTests?.RootElement.Clone(),
            output?.RootElement.Clone(),
            run?.RootElement.Clone());

        var names = await UserProfiles.FindDisplayNamesAsync(connection, [stored.RequestedBy], cancellationToken);
        return response with { RequestedByName = names.GetValueOrDefault(stored.RequestedBy) ?? stored.RequestedByName };
    }

    private static IResult NoRequestProblem() => StudioEndpoints.NotFound(NoRequest);
}

/// <summary>
/// A row of <c>axis.publish_requests</c> as <see cref="StudioPublishEndpoints.FindRequestAsync"/> reads it, without its snapshot.
/// <c>Diagnostics</c>, <c>ScenarioResults</c>, <c>PackageTests</c>, <c>Output</c> and <c>Run</c> are the stored JSON text, null when none were stored.
/// <c>Package</c> is the package a package <c>test</c> tests, a <c>build</c> builds or a <c>run</c> runs, null for a scenario test and for every other kind.
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
    Guid? DraftId,
    string? Package,
    string? PackageTests,
    string? Output,
    string? Run);

/// <summary>
/// A publish request as the authoring API shows it. <c>Application</c> is the manifest name, <c>Target</c> is
/// <c>tenant</c> or <c>preview</c> and <c>Diagnostics</c> is empty until the request ends. <c>Scenarios</c> holds the stored
/// results of a <c>test</c>, <c>publish</c> or <c>validate</c>, and is null when none were stored. For a scenario <c>test</c> it is
/// empty until the request ends, and for a package <c>test</c> it is null, as no scenario runs. <c>Package</c> is the package a package <c>test</c>
/// tests, a <c>build</c> builds or a <c>run</c> runs, null for every other request. <c>PackageTests</c> holds the outcome of each test of a package <c>test</c>, an empty list
/// until the request ends, and is null for every other kind. <c>Output</c> holds the lines the package host wrote for a <c>build</c>, a <c>run</c> or a package
/// <c>test</c>, an empty list until the request ends, and is null for every other kind. <c>Run</c> holds the call of a <c>run</c>
/// (<c>operation</c>, <c>outcome</c>, <c>output</c>, <c>error</c> and <c>durationMs</c>) once it ends, and is null until then and for every other kind. <c>RequestedByName</c> is the name in the requesting user's profile, else the one stored
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
    JsonElement? Scenarios,
    string? Package,
    JsonElement? PackageTests,
    JsonElement? Output,
    JsonElement? Run);
