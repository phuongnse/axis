using System.Text;
using Axis.Server.Studio;
using Axis.Studio;

namespace Axis.Server.Mcp;

/// <summary>
/// The package file tools of the MCP server, <c>list_package_files</c>, <c>read_package_file</c>, <c>write_package_file</c> and
/// <c>delete_package_file</c>, see <c>docs/reference/mcp-server.md#tools</c>. A package file is a file under <c>packages/</c> of the
/// application's folder. The two read tools need the author role only, and read the working folder or, with a draft, the person's
/// own draft. The two write tools change only the person's own draft, through <see cref="StudioDrafts"/>, and need the developer
/// role and the tenant's application packages switch on, the check of <see cref="StudioEndpoints.DeveloperRefusal"/> that the
/// package file routes make, see <c>docs/reference/studio-packages.md#the-authoring-api</c>. They make one commit on the draft's
/// branch and answer what the routes answer for a draft, or the route's problem title. Each write or delete appends one
/// <c>mcp.toolCalled</c> audit record that holds ids only, with no package, path or content, see
/// <c>docs/reference/storage.md#audit-actions</c>.
/// </summary>
internal static class McpPackageFileTools
{
    /// <summary>The package files of the application, as <c>GET .../package-files</c> lists them. A draft records no version.</summary>
    public static async Task<(object? Output, string? Error)> ListAsync(McpToolContext context, string application, string? draft, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        if (draft is null)
        {
            return (Listed(StudioPackageFiles.List(folder)), null);
        }

        var (drafts, error) = McpReadTools.Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var listed = await drafts.ListPackageFilesAsync(draft, ct);
        return listed.Refusal is null ? (Listed(listed.Value!), null) : (null, McpReadTools.DraftError(listed.Refusal));
    }

    /// <summary>One package file of the application, as <c>GET .../packages/{package}/files/{path}</c> reads it.</summary>
    public static async Task<(object? Output, string? Error)> ReadAsync(
        McpToolContext context, string application, string package, string path, string? draft, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        if (StudioPackageFiles.File(package, path) is not { } file)
        {
            return (null, StudioPackageFileEndpoints.NoPackageFile);
        }

        if (draft is null)
        {
            return StudioPackageFiles.Read(folder, file) is { } found
                ? (StudioPackageFileEndpoints.Response(package, path, found), null)
                : (null, StudioPackageFileEndpoints.NoPackageFile);
        }

        var (drafts, error) = McpReadTools.Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var read = await drafts.ReadPackageFileAsync(draft, file, ct);
        return read.Refusal switch
        {
            null => (StudioPackageFileEndpoints.Response(package, path, read.Value!), null),
            StudioWriteRefusal.FileMissing => (null, StudioPackageFileEndpoints.NoPackageFile),
            _ => (null, McpReadTools.DraftError(read.Refusal)),
        };
    }

    /// <summary>
    /// Creates or replaces one package file in the draft, as the <c>PUT</c> route does for a draft, but without a version: the
    /// file is written whether or not the draft has it. Answers the file's new version and the draft's diagnostics.
    /// </summary>
    public static async Task<(object? Output, string? Error)> WriteAsync(
        McpToolContext context, string application, string draft, string package, string path, string content, CancellationToken ct)
    {
        if (StudioEndpoints.DeveloperRefusal(context.Options, context.Caller.Roles, context.ApplicationPackages) is { } denied)
        {
            return (null, denied);
        }

        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        if (StudioPackageFiles.File(package, path) is not { } file)
        {
            return (null, StudioPackageFileEndpoints.BadPath);
        }

        var (drafts, error) = McpReadTools.Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var written = await drafts.WritePackageFileAsync(draft, file, Encoding.UTF8.GetBytes(content), null, false, ct);
        if (Refused(written.Refusal) is { } refused)
        {
            return (null, refused);
        }

        await McpWriteTools.AuditAsync(context, folder, "write_package_file", draft, null);
        var (diagnostics, diagnosticsError) = await McpWriteTools.DiagnosticsAsync(context, folder, draft, ct);
        return diagnostics is null ? (null, diagnosticsError) : (new StudioPatchResponse(written.Value!, diagnostics), null);
    }

    /// <summary>Removes one package file from the draft, as the <c>DELETE</c> route does for a draft, without a version, and answers the draft's diagnostics.</summary>
    public static async Task<(object? Output, string? Error)> DeleteAsync(
        McpToolContext context, string application, string draft, string package, string path, CancellationToken ct)
    {
        if (StudioEndpoints.DeveloperRefusal(context.Options, context.Caller.Roles, context.ApplicationPackages) is { } denied)
        {
            return (null, denied);
        }

        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, McpReadTools.Title(refusal));
        }

        if (StudioPackageFiles.File(package, path) is not { } file)
        {
            return (null, StudioPackageFileEndpoints.NoPackageFile);
        }

        var (drafts, error) = McpReadTools.Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var deleted = await drafts.DeletePackageFileAsync(draft, file, null, ct);
        if (Refused(deleted.Refusal) is { } refused)
        {
            return (null, refused);
        }

        await McpWriteTools.AuditAsync(context, folder, "delete_package_file", draft, null);
        var (diagnostics, diagnosticsError) = await McpWriteTools.DiagnosticsAsync(context, folder, draft, ct);
        return diagnostics is null ? (null, diagnosticsError) : (new StudioDeleteResponse(diagnostics), null);
    }

    private static StudioPackageFileListResponse Listed(IEnumerable<(string Package, string Path, string Version)> items) =>
        new([.. items.Select(item => new StudioPackageFileItem(item.Package, item.Path, item.Version))]);

    // The title of a refused draft write, or null when the write was made. A refusal that no case names is a draft that is not found.
    private static string? Refused(StudioWriteRefusal? refusal) => refusal switch
    {
        null => null,
        StudioWriteRefusal.Unavailable => StudioEndpoints.WriteLockUnavailable,
        StudioWriteRefusal.Stale or StudioWriteRefusal.Exists => StudioPackageFileEndpoints.StalePackageFile,
        StudioWriteRefusal.FileMissing => StudioPackageFileEndpoints.NoPackageFile,
        _ => StudioRevisionEndpoints.NoDraft,
    };
}
