using System.Text;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Server.Authentication;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Axis.Studio;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Axis.Server.Mcp;

/// <summary>
/// What a tool needs from the request: the tenant's Studio options, the person, the installed packages and modules, the write lock,
/// and the tenant database and loggers that a write tool needs for its audit record and its revisions.
/// </summary>
internal sealed record McpToolContext(
    StudioOptions Options,
    EndpointCallerIdentity Caller,
    PackageCatalog Packages,
    ModuleCatalog Modules,
    IStudioWriteLock WriteLock,
    TenantDatabase Database,
    ILoggerFactory Loggers);

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
        McpToolContext context, string application, string? draft, CancellationToken ct)
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
        McpToolContext context, string application, string id, string? draft, CancellationToken ct)
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
    public static (object? Output, string? Error) ReadModuleResource(McpToolContext context, string application, string module, string id)
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
        McpToolContext context, string application, string? draft, CancellationToken ct)
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

    private static CompilationResult Compile(McpToolContext context, IReadOnlyList<(string File, byte[] Bytes)> files) =>
        ApplicationCompiler.CompileFiles(Contents(files), context.Packages.Versions, context.Modules.Modules);

    /// <summary>The files of a draft as compile input.</summary>
    internal static IReadOnlyList<ResourceContent> Contents(IReadOnlyList<(string File, byte[] Bytes)> files) =>
        [.. files.Select(file => new ResourceContent(file.File, Encoding.UTF8.GetString(file.Bytes)))];

    /// <summary>The files of the person's draft as its branch holds them, or the title that says why not.</summary>
    internal static async Task<(IReadOnlyList<(string File, byte[] Bytes)>? Files, string? Error)> DraftFilesAsync(
        McpToolContext context, string folder, string draft, CancellationToken ct)
    {
        var (drafts, error) = Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var outcome = await drafts.FilesAsync(draft, ct);
        return outcome.Refusal is null ? (outcome.Value, null) : (null, DraftError(outcome.Refusal));
    }

    /// <summary>The drafts of the calling person when the draft exists and is theirs, else the title that says why not.</summary>
    internal static async Task<(StudioDrafts? Drafts, string? Error)> FindDraftAsync(
        McpToolContext context, string folder, string draft, CancellationToken ct)
    {
        var (drafts, error) = Drafts(context, folder);
        if (drafts is null)
        {
            return (null, error);
        }

        var outcome = await drafts.FindAsync(draft, ct);
        return outcome.Refusal is null ? (drafts, null) : (null, DraftError(outcome.Refusal));
    }

    internal static (StudioDrafts? Drafts, string? Error) Drafts(McpToolContext context, string folder) =>
        StudioDrafts.For(context.Options.RevisionsPath, folder, context.Caller.Id, context.Caller.DisplayName, context.WriteLock) is { } drafts
            ? (drafts, null)
            : (null, StudioRevisionEndpoints.NoDraft);

    /// <summary>The titles of the draft diff route: the write lock when it cannot be taken, else no draft.</summary>
    internal static string DraftError(StudioWriteRefusal? refusal) =>
        refusal == StudioWriteRefusal.Unavailable ? StudioEndpoints.WriteLockUnavailable : StudioRevisionEndpoints.NoDraft;

    /// <summary>The title of a refusal of the authoring API, or a fixed text. Never an exception message or a path.</summary>
    internal static string Title(IResult? refusal) =>
        refusal is ProblemHttpResult { ProblemDetails.Title: { } title } ? title : Refused;
}
