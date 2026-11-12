using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Data.Audit;
using Axis.Server.Studio;
using Axis.Studio;
using Json.Pointer;
using Npgsql;

namespace Axis.Server.Mcp;

/// <summary>
/// The tools of the MCP server that change a draft, <c>create_draft</c>, <c>write_resource</c>, <c>delete_resource</c> and <c>write_layer</c>, and
/// <c>check_expression</c>, <c>validate_draft</c> and <c>diff_draft</c>, which write nothing and record no audit record, see
/// <c>docs/reference/mcp-server.md#tools</c>. They change only the draft of the
/// calling person through <see cref="StudioDrafts"/> and answer what the authoring API's matching route answers, or an error text
/// that is a problem title of the authoring API or a fixed one, never text from the request or a path. A draft that is not the
/// person's, or does not exist, answers the draft routes' title and changes nothing. Each successful write call appends one
/// <c>mcp.toolCalled</c> audit record that holds ids only, see <c>docs/reference/storage.md#audit-actions</c>. <c>run_scenarios</c>
/// changes no draft. It stores a <c>test</c> request of the draft's files, or reads one back, and records no audit record, as a
/// <c>test</c> records none, see <c>docs/reference/authoring.md#test</c>. <c>accept_draft</c> changes no draft either. It stores an
/// <c>accept</c> publish request of the draft, only under the rules of <c>docs/reference/authoring-requests.md#accepting</c>, and
/// appends one <c>mcp.toolCalled</c> audit record only when it queues an accept.
/// </summary>
internal static class McpWriteTools
{
    private const string NoHistory = "The application has no history to start a draft from.";

    /// <summary>The answer to <c>create_draft</c>: the draft's id and the commit of the working folder's history it starts from.</summary>
    internal sealed record CreatedDraft(string Id, string Start);

    /// <summary>Makes a draft of the person at the tip of the application's history.</summary>
    public static async Task<(object? Output, string? Error)> CreateDraftAsync(McpToolContext context, string application, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var caller = context.Caller;
        var drafts = StudioDrafts.For(context.Options.RevisionsPath, folder, caller.Id, caller.DisplayName, context.WriteLock);
        var revisions = StudioRevisions.For(
            context.Options.RevisionsPath, folder, caller.Id, caller.DisplayName, context.Loggers.CreateLogger<StudioRevisions>());
        if (drafts is null || revisions is null)
        {
            return (null, NoHistory);
        }

        var outcome = await drafts.CreateAsync(revisions, ct);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return (null, StudioEndpoints.WriteLockUnavailable);
            case not null:
                return (null, NoHistory);
        }

        await AuditAsync(context, folder, "create_draft", outcome.Value!.Id, null);
        return (new CreatedDraft(outcome.Value.Id, outcome.Value.Start), null);
    }

    /// <summary>
    /// Creates or replaces one resource in the draft, as the create route and the draft's replace do. A resource that has
    /// diagnostics is still saved, and the answer lists the draft's diagnostics.
    /// </summary>
    public static async Task<(object? Output, string? Error)> WriteResourceAsync(
        McpToolContext context, string application, string draft, string contentJson, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var (drafts, error) = await McpReadTools.FindDraftAsync(context, folder, draft, ct);
        if (drafts is null)
        {
            return (null, error);
        }

        var content = JsonNode.Parse(contentJson)!.AsObject();
        var listed = await drafts.ListAsync(draft, ct);
        if (listed.Refusal is not null)
        {
            return (null, McpReadTools.DraftError(listed.Refusal));
        }

        // A resource the draft lacks is a create: its id, kind and name get the create route's own checks.
        var exists = content["id"] is JsonValue idValue
            && idValue.TryGetValue<string>(out var id)
            && StudioEndpoints.ParseResourceId(id) is { } resource
            && listed.Value!.Any(file => file.Id == resource);
        if (!exists && StudioFileNames.NewFile(content, out _, out var problem) is null)
        {
            return (null, problem);
        }

        var outcome = await drafts.WriteAsync(draft, content, ct);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.DoesNotApply:
                return (null, "The body is not a resource.");
            case StudioWriteRefusal.Exists:
                return (null, "A resource with this id or file already exists.");
            case StudioWriteRefusal.UnsafePath:
                return (null, "The file name leads outside the application folder.");
            case StudioWriteRefusal.ChangesIdOrKind:
                return (null, "A patch may not change id or kind.");
            case StudioWriteRefusal.Unavailable:
                return (null, StudioEndpoints.WriteLockUnavailable);
            case not null:
                return (null, StudioRevisionEndpoints.NoDraft);
        }

        var written = outcome.Value!;
        await AuditAsync(context, folder, "write_resource", draft, written.Id);

        var (diagnostics, diagnosticsError) = await DiagnosticsAsync(context, folder, draft, ct);
        if (diagnostics is null)
        {
            return (null, diagnosticsError);
        }

        return (new StudioCreateResponse(written.Id.ToString("D"), written.Kind, written.Name ?? "", written.File, written.Version, diagnostics), null);
    }

    /// <summary>Removes one resource from the draft, as the delete route does, and answers the draft's diagnostics.</summary>
    public static async Task<(object? Output, string? Error)> DeleteResourceAsync(
        McpToolContext context, string application, string draft, string id, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var (drafts, error) = await McpReadTools.FindDraftAsync(context, folder, draft, ct);
        if (drafts is null)
        {
            return (null, error);
        }

        if (StudioEndpoints.ParseResourceId(id) is not { } resource)
        {
            return (null, StudioEndpoints.NoResource);
        }

        // The draft is known, so a refusal as NotFound is the resource.
        var outcome = await drafts.DeleteAsync(draft, resource, ct);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return (null, StudioEndpoints.WriteLockUnavailable);
            case StudioWriteRefusal.Manifest:
                return (null, "The manifest cannot be created or deleted.");
            case not null:
                return (null, StudioEndpoints.NoResource);
        }

        await AuditAsync(context, folder, "delete_resource", draft, resource);

        var (diagnostics, diagnosticsError) = await DiagnosticsAsync(context, folder, draft, ct);
        if (diagnostics is null)
        {
            return (null, diagnosticsError);
        }

        return (new StudioDeleteResponse(diagnostics), null);
    }

    /// <summary>
    /// Writes the edited module resource <paramref name="contentJson"/> as the draft's layer that holds only the difference from the
    /// module version the draft's manifest pins, through the checks of the save route, <see cref="StudioEndpoints.LayerChanges"/>.
    /// An edit with no difference removes the draft's layer and answers no file and no version.
    /// </summary>
    public static async Task<(object? Output, string? Error)> WriteLayerAsync(
        McpToolContext context, string application, string draft, string module, string id, string contentJson, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var (files, error) = await McpReadTools.DraftFilesAsync(context, folder, draft, ct);
        if (files is null)
        {
            return (null, error);
        }

        var (found, notFound) = StudioEndpoints.FindModuleFile(
            ApplicationCompiler.CompileFiles(McpReadTools.Contents(files), context.Packages.Versions, context.Modules.Modules),
            context.Modules,
            module,
            id);
        if (found is null)
        {
            return (null, McpReadTools.Title(notFound));
        }

        using var edited = JsonDocument.Parse(contentJson);
        var (changes, layerRefusal) = StudioEndpoints.LayerChanges(found, module, edited.RootElement);
        if (layerRefusal is not null)
        {
            return (null, layerRefusal);
        }

        var (drafts, draftsError) = McpReadTools.Drafts(context, folder);
        if (drafts is null)
        {
            return (null, draftsError);
        }

        var outcome = await drafts.WriteLayerAsync(draft, module, found.Name!, changes, ct);
        switch (outcome.Refusal)
        {
            case StudioWriteRefusal.Unavailable:
                return (null, StudioEndpoints.WriteLockUnavailable);
            case StudioWriteRefusal.Exists:
                return (null, "A resource with this id or file already exists.");
            case not null:
                return (null, StudioRevisionEndpoints.NoDraft);
        }

        await AuditAsync(context, folder, "write_layer", draft, found.Id);

        var (diagnostics, diagnosticsError) = await DiagnosticsAsync(context, folder, draft, ct);
        if (diagnostics is null)
        {
            return (null, diagnosticsError);
        }

        return (new StudioModuleSaveResponse(outcome.Value!.File, outcome.Value.Version, diagnostics), null);
    }

    /// <summary>
    /// Checks an expression at a path of a resource of the draft, as <c>POST .../expressions/check</c> does for the working folder,
    /// through the same <see cref="StudioExpressionEndpoints.Check"/>. Writes nothing and records no audit record.
    /// </summary>
    public static async Task<(object? Output, string? Error)> CheckExpressionAsync(
        McpToolContext context, string application, string draft, string id, string path, string expression, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var (files, error) = await McpReadTools.DraftFilesAsync(context, folder, draft, ct);
        if (files is null)
        {
            return (null, error);
        }

        if (StudioEndpoints.ParseResourceId(id) is not { } resource || !JsonPointer.TryParse(path, out var pointer))
        {
            return (null, StudioExpressionEndpoints.BadCheck);
        }

        var (answer, problem) = StudioExpressionEndpoints.Check(
            StudioFolder.Resources(files), McpReadTools.Contents(files), resource, path, pointer, expression, context.Packages, context.Modules);
        return problem is null ? (answer, null) : (null, problem);
    }

    /// <summary>
    /// Answers the compile diagnostics of the draft, the module contract checks included, as the diagnostics route answers them for
    /// the working folder, see <c>docs/reference/authoring.md#diagnostics</c>. Writes nothing and records no audit record.
    /// </summary>
    public static async Task<(object? Output, string? Error)> ValidateDraftAsync(
        McpToolContext context, string application, string draft, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var (diagnostics, error) = await DiagnosticsAsync(context, folder, draft, ct);
        return diagnostics is null ? (null, error) : (new StudioDiagnosticsResponse(diagnostics), null);
    }

    /// <summary>
    /// Answers one page of the draft's diff through <see cref="StudioDrafts.DiffAsync"/>, with the titles of the draft diff route, see
    /// <c>docs/reference/authoring-revisions.md#draft-diff</c>. Writes nothing and records no audit record.
    /// </summary>
    public static async Task<(object? Output, string? Error)> DiffDraftAsync(
        McpToolContext context, string application, string draft, string? cursor, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        var (drafts, error) = McpReadTools.Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var outcome = await drafts.DiffAsync(draft, cursor, ct);
        return outcome.Refusal switch
        {
            null => (outcome.Value, null),
            StudioWriteRefusal.Unavailable => (null, StudioEndpoints.WriteLockUnavailable),
            StudioWriteRefusal.DoesNotApply => (null, StudioRevisionEndpoints.ForeignCursor),
            _ => (null, StudioRevisionEndpoints.NoDraft),
        };
    }

    /// <summary>
    /// The answer of <c>run_scenarios</c>: the request's id and status, its diagnostics and its scenario results, each an
    /// empty list until the publisher has run the request.
    /// </summary>
    internal sealed record McpScenarioRun(string Request, string Status, JsonElement Diagnostics, JsonElement Scenarios);

    /// <summary>
    /// Without <paramref name="request"/>, stores a <c>test</c> publish request whose snapshot is the files of the person's draft, with
    /// the draft in <c>draft_id</c> and the scenario <paramref name="names"/> (a JSON list, none or empty for every scenario), and answers
    /// it queued. With <paramref name="request"/>, answers that request, only when it is a <c>test</c> of this draft by this person. The
    /// working folder plays no part. See <c>docs/reference/authoring.md#test</c>.
    /// </summary>
    public static async Task<(object? Output, string? Error)> RunScenariosAsync(
        McpToolContext context, string application, string draft, string? names, string? request, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        // A draft id that passed the check is a "D" GUID.
        if (request is not null)
        {
            var (drafts, draftError) = await McpReadTools.FindDraftAsync(context, folder, draft, ct);
            return drafts is null ? (null, draftError) : await ReadRunAsync(context, folder, Guid.Parse(draft), request, ct);
        }

        var (files, error) = await McpReadTools.DraftFilesAsync(context, folder, draft, ct);
        if (files is null)
        {
            return (null, error);
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return (null, StudioPublishEndpoints.NoManifestId);
        }

        var scenarioNames = names is null || JsonNode.Parse(names)!.AsArray().Count == 0 ? null : names;
        var id = Guid.CreateVersion7();
        var connection = await context.Database.GetConnectionAsync(ct);
        await StudioPublishEndpoints.InsertRequestAsync(
            connection,
            id,
            "test",
            applicationId,
            "tenant",
            StudioPublishEndpoints.SnapshotJson(StudioFolder.Snapshot(files)),
            releaseId: null,
            label: null,
            note: null,
            context.Caller.Id,
            scenarioNames,
            Guid.Parse(draft),
            folder: null,
            client: null,
            ct);
        return (new McpScenarioRun(id.ToString("D"), "queued", EmptyList(), EmptyList()), null);
    }

    // Only a test of this draft by the calling person is answered. Anything else counts as unknown.
    private static async Task<(object? Output, string? Error)> ReadRunAsync(
        McpToolContext context, string folder, Guid draft, string request, CancellationToken ct)
    {
        if (!Guid.TryParseExact(request, "D", out var requestId)
            || StudioFolder.ReadManifest(folder) is not { Id: { } applicationId }
            || await StudioPublishEndpoints.FindRequestAsync(
                await context.Database.GetConnectionAsync(ct), applicationId, requestId, ct) is not { Kind: "test" } stored
            || stored.RequestedBy != context.Caller.Id
            || stored.DraftId != draft)
        {
            return (null, StudioPublishEndpoints.NoRequest);
        }

        using var diagnostics = JsonDocument.Parse(stored.Diagnostics ?? "[]");
        using var scenarios = JsonDocument.Parse(stored.ScenarioResults ?? "[]");
        return (new McpScenarioRun(request, stored.Status, diagnostics.RootElement.Clone(), scenarios.RootElement.Clone()), null);
    }

    /// <summary>The answer of <c>accept_draft</c>: the accept's id and status and its diagnostics, an empty list until the publisher has run it.</summary>
    internal sealed record McpAccept(string Request, string Status, JsonElement Diagnostics);

    private const string NoReadyRequest = "No ready authoring request of yours has this draft.";

    // The newest check step and the newest review step of the request both have the outcome passed, and the review comes after the check.
    private const string LastRoundPassed =
        """
        SELECT coalesce(
            (SELECT outcome = 'passed' FROM axis.authoring_request_steps WHERE request_id = @request AND kind = 'check' ORDER BY ordinal DESC LIMIT 1)
            AND (SELECT outcome = 'passed' FROM axis.authoring_request_steps WHERE request_id = @request AND kind = 'review' ORDER BY ordinal DESC LIMIT 1)
            AND (SELECT max(ordinal) FROM axis.authoring_request_steps WHERE request_id = @request AND kind = 'review')
                > (SELECT max(ordinal) FROM axis.authoring_request_steps WHERE request_id = @request AND kind = 'check'),
            false)
        """;

    /// <summary>
    /// Without <paramref name="request"/>, stores an <c>accept</c> publish request of the draft with the calling client, only when the
    /// draft belongs to a <c>ready</c> authoring request of the person, the application's auto-accept is on, that request asked for it
    /// and its last check and review passed. It answers the accept already queued or running instead of storing a second one. With
    /// <paramref name="request"/>, answers that request, only when it is an <c>accept</c> of this draft by this person, and the draft need not exist any more. A refusal is a
    /// fixed text that names the rule and stores nothing. See <c>docs/reference/authoring-requests.md#accepting</c>.
    /// </summary>
    public static async Task<(object? Output, string? Error)> AcceptDraftAsync(
        McpToolContext context, string application, string draft, string? request, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return (null, StudioPublishEndpoints.NoManifestId);
        }

        var connection = await context.Database.GetConnectionAsync(ct);
        if (request is not null)
        {
            // Reading an accept needs no draft: the publisher holds the folder's write lock while it runs the accept, and a successful accept removes the draft.
            return Guid.TryParseExact(draft, "D", out var accepted)
                ? await ReadAcceptAsync(context, connection, applicationId, accepted, request, ct)
                : (null, StudioRevisionEndpoints.NoDraft);
        }

        var (drafts, draftError) = await McpReadTools.FindDraftAsync(context, folder, draft, ct);
        if (drafts is null)
        {
            return (null, draftError);
        }

        // A draft id that passed the check is a "D" GUID.
        var draftId = Guid.Parse(draft);

        await using var transaction = await connection.BeginTransactionAsync(ct);
        var (requestId, autoAccept) = await FindReadyRequestAsync(connection, transaction, applicationId, draftId, context.Caller.Id, ct);
        var refused = requestId is null
            ? NoReadyRequest
            : !await StudioSettingsEndpoints.ReadAutoAcceptAsync(connection, transaction, applicationId, ct)
                ? "The application does not allow auto-accept."
                : !autoAccept
                    ? "The authoring request did not ask for auto-accept."
                    : !await LastRoundPassedAsync(connection, transaction, requestId.Value, ct)
                        ? "The last check and review of the authoring request did not both pass."
                        : null;
        if (refused is not null)
        {
            await transaction.RollbackAsync(ct);
            return (null, refused);
        }

        if (await StudioAuthoringRequestEndpoints.FindPendingAcceptAsync(connection, transaction, draftId, ct) is { } pending)
        {
            await transaction.RollbackAsync(ct);
            return await ReadAcceptAsync(context, connection, applicationId, draftId, pending.ToString("D"), ct);
        }

        var id = Guid.CreateVersion7();
        await StudioPublishEndpoints.InsertRequestAsync(
            connection,
            id,
            "accept",
            applicationId,
            "tenant",
            snapshot: null,
            releaseId: null,
            label: null,
            note: null,
            context.Caller.Id,
            scenarioNames: null,
            draftId,
            folder: application,
            client: context.Caller.ClientId,
            ct);
        await transaction.CommitAsync(ct);
        await AuditAsync(context, folder, "accept_draft", draft, null);
        return (new McpAccept(id.ToString("D"), "queued", EmptyList()), null);
    }

    // The request that holds the draft: the person's own, ready, row-locked so that no second call or route moves it meanwhile.
    private static async Task<(Guid? Id, bool AutoAccept)> FindReadyRequestAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid applicationId, Guid draftId, string author, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, auto_accept FROM axis.authoring_requests
            WHERE application_id = @application AND draft_id = @draft AND author = @author AND state = 'ready'
            ORDER BY sent_at DESC, id DESC LIMIT 1 FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("draft", draftId);
        command.Parameters.AddWithValue("author", author);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetBoolean(1)) : (null, false);
    }

    private static async Task<bool> LastRoundPassedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(LastRoundPassed, connection, transaction);
        command.Parameters.AddWithValue("request", requestId);
        return await command.ExecuteScalarAsync(ct) is true;
    }

    // Only an accept of this draft by the calling person is answered. Anything else counts as unknown.
    private static async Task<(object? Output, string? Error)> ReadAcceptAsync(
        McpToolContext context, NpgsqlConnection connection, Guid applicationId, Guid draft, string request, CancellationToken ct)
    {
        if (!Guid.TryParseExact(request, "D", out var requestId)
            || await StudioPublishEndpoints.FindRequestAsync(connection, applicationId, requestId, ct) is not { Kind: "accept" } stored
            || stored.RequestedBy != context.Caller.Id
            || stored.DraftId != draft)
        {
            return (null, StudioPublishEndpoints.NoRequest);
        }

        using var diagnostics = JsonDocument.Parse(stored.Diagnostics ?? "[]");
        return (new McpAccept(request, stored.Status, diagnostics.RootElement.Clone()), null);
    }

    private static JsonElement EmptyList() => JsonDocument.Parse("[]").RootElement.Clone();

    // The compile diagnostics of the draft as its branch now holds it.
    private static async Task<(IReadOnlyList<StudioDiagnostic>? Diagnostics, string? Error)> DiagnosticsAsync(
        McpToolContext context, string folder, string draft, CancellationToken ct)
    {
        var (files, error) = await McpReadTools.DraftFilesAsync(context, folder, draft, ct);
        return files is null
            ? (null, error)
            : (StudioDiagnostics.CompileFiles(McpReadTools.Contents(files), context.Packages, context.Modules), null);
    }

    // Appends the one audit record of a successful write call, in its own transaction right after the draft's commit. It takes
    // no cancellation token, so a request that is cancelled after the commit still leaves its record. It holds ids only: never
    // content, a name or a path.
    private static async Task AuditAsync(McpToolContext context, string folder, string tool, string draft, Guid? resource)
    {
        var ct = CancellationToken.None;
        var connection = await context.Database.GetConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                context.Caller.Id,
                AuditActions.McpToolCalled,
                StudioFolder.ReadManifest(folder)?.Id,
                null,
                null,
                null,
                new JsonObject
                {
                    ["tool"] = tool,
                    ["clientId"] = context.Caller.ClientId,
                    ["draftId"] = draft,
                    ["resourceId"] = resource?.ToString("D"),
                }),
            ct);
        await transaction.CommitAsync(ct);
    }
}
