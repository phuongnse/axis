using System.Text;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Server.Authentication;
using Axis.Server.Studio;
using Axis.Studio;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Axis.Server.Mcp;

/// <summary>What a read tool needs from the request: the tenant's Studio options, the person and the installed packages and modules.</summary>
internal sealed record McpReadContext(
    StudioOptions Options, EndpointCallerIdentity Caller, PackageCatalog Packages, ModuleCatalog Modules, IStudioWriteLock WriteLock);

/// <summary>
/// The read tools of the MCP server, see <c>docs/reference/mcp-server.md#tools</c>. Each answers the output of the matching
/// authoring API route, built by the helper the route calls, or an error text, which is the problem title of the route's
/// refusal and never text from the request or a path. Without a draft a tool reads the working folder. With a draft it reads the
/// draft of the calling person through <see cref="StudioDrafts"/>, and answers the draft routes' titles when the server keeps no
/// revisions, the draft is not the person's or the write lock cannot be taken. See <c>docs/reference/revisions-and-drafts.md#drafts</c>.
/// </summary>
internal static class McpReadTools
{
    private const string Refused = "The call was refused.";

    /// <summary>The resources of the application, as <c>GET .../resources</c> lists them. A draft records no version.</summary>
    public static async Task<(object? Output, string? Error)> ListResourcesAsync(
        McpReadContext context, string application, string? draft, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, Title(refusal));
        }

        if (draft is null)
        {
            return (StudioEndpoints.ListResources(StudioFolder.List(folder), StudioReferences.Compile(folder, context.Packages, context.Modules)), null);
        }

        var (files, error) = await DraftFilesAsync(context, folder, draft, ct);
        return files is null
            ? (null, error)
            : (StudioEndpoints.ListResources(StudioFolder.Resources(files), Compile(context, files).References), null);
    }

    /// <summary>One resource of the application, as <c>GET .../resources/{id}</c> reads it. A draft records the version the resource has on <c>main</c>.</summary>
    public static async Task<(object? Output, string? Error)> ReadResourceAsync(
        McpReadContext context, string application, string id, string? draft, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, Title(refusal));
        }

        if (draft is null)
        {
            return StudioEndpoints.FindResource(StudioFolder.List(folder), id) is { } found
                ? (StudioEndpoints.ReadResource(found), null)
                : (null, StudioEndpoints.NoResource);
        }

        var (drafts, error) = await FindDraftAsync(context, folder, draft, ct);
        if (drafts is null)
        {
            return (null, error);
        }

        if (StudioEndpoints.ParseResourceId(id) is not { } resource)
        {
            return (null, StudioEndpoints.NoResource);
        }

        var outcome = await drafts.ReadAsync(draft, resource, ct);
        return outcome.Refusal switch
        {
            null => (StudioEndpoints.ReadResource(outcome.Value!), null),
            StudioWriteRefusal.NotFound => (null, StudioEndpoints.NoResource),
            _ => (null, DraftError(outcome.Refusal)),
        };
    }

    /// <summary>One resource of the module version the application pins, with its layer applied, as <c>GET .../modules/{module}/resources/{id}</c> reads it.</summary>
    public static (object? Output, string? Error) ReadModuleResource(McpReadContext context, string application, string module, string id)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, Title(refusal));
        }

        var (resource, notFound) = StudioEndpoints.ReadModuleResource(folder, module, id, context.Packages, context.Modules);
        return resource is null ? (null, Title(notFound)) : (resource, null);
    }

    /// <summary>The names the application defines, as <c>GET .../names</c> answers them. A draft records no version.</summary>
    public static async Task<(object? Output, string? Error)> ReadNamesAsync(
        McpReadContext context, string application, string? draft, CancellationToken ct)
    {
        var (folder, refusal) = StudioEndpoints.FindFolder(context.Options, application);
        if (folder is null)
        {
            return (null, Title(refusal));
        }

        if (draft is null)
        {
            return (ApplicationCompiler.Compile(folder, context.Packages.Versions, context.Modules.Modules).Names, null);
        }

        var (files, error) = await DraftFilesAsync(context, folder, draft, ct);
        return files is null ? (null, error) : (Compile(context, files).Names, null);
    }

    private static CompilationResult Compile(McpReadContext context, IReadOnlyList<(string File, byte[] Bytes)> files) =>
        ApplicationCompiler.CompileFiles(
            [.. files.Select(file => new ResourceContent(file.File, Encoding.UTF8.GetString(file.Bytes)))],
            context.Packages.Versions,
            context.Modules.Modules);

    private static async Task<(IReadOnlyList<(string File, byte[] Bytes)>? Files, string? Error)> DraftFilesAsync(
        McpReadContext context, string folder, string draft, CancellationToken ct)
    {
        var (drafts, error) = Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var outcome = await drafts.FilesAsync(draft, ct);
        return outcome.Refusal is null ? (outcome.Value, null) : (null, DraftError(outcome.Refusal));
    }

    // The drafts of the calling person when the draft exists and is theirs, else the title that says why not.
    private static async Task<(StudioDrafts? Drafts, string? Error)> FindDraftAsync(
        McpReadContext context, string folder, string draft, CancellationToken ct)
    {
        var (drafts, error) = Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var outcome = await drafts.FindAsync(draft, ct);
        return outcome.Refusal is null ? (drafts, null) : (null, DraftError(outcome.Refusal));
    }

    private static (StudioDrafts? Drafts, string? Error) Drafts(McpReadContext context, string folder) =>
        StudioDrafts.For(context.Options.RevisionsPath, folder, context.Caller.Id, context.Caller.DisplayName, context.WriteLock) is { } drafts
            ? (drafts, null)
            : (null, StudioRevisionEndpoints.NoDraft);

    // The titles of the draft diff route: the write lock when it cannot be taken, else no draft.
    private static string DraftError(StudioWriteRefusal? refusal) =>
        refusal == StudioWriteRefusal.Unavailable ? StudioEndpoints.WriteLockUnavailable : StudioRevisionEndpoints.NoDraft;

    // The title of a refusal of the authoring API, or a fixed text. Never an exception message or a path.
    private static string Title(IResult? refusal) =>
        refusal is ProblemHttpResult { ProblemDetails.Title: { } title } ? title : Refused;
}
