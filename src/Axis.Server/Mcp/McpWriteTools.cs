using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Server.Studio;
using Axis.Studio;
using Json.Pointer;

namespace Axis.Server.Mcp;

/// <summary>
/// The tools of the MCP server that change a draft, <c>create_draft</c>, <c>write_resource</c> and <c>delete_resource</c>, and
/// <c>check_expression</c>, which writes nothing, see <c>docs/reference/mcp-server.md#tools</c>. They change only the draft of the
/// calling person through <see cref="StudioDrafts"/> and answer what the authoring API's matching route answers, or an error text
/// that is a problem title of the authoring API or a fixed one, never text from the request or a path. A draft that is not the
/// person's, or does not exist, answers the draft routes' title and changes nothing. Each successful write call appends one
/// <c>mcp.toolCalled</c> audit record that holds ids only, see <c>docs/reference/storage.md#audit-actions</c>.
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
