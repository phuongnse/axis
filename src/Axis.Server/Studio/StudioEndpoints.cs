using System.Text.Json;
using Axis.Server.Http;
using Axis.Server.Users;

namespace Axis.Server.Studio;

/// <summary>
/// The read side of the authoring API, for an author who edits the application folders in
/// <c>Studio:Folders</c>. <c>GET /api/studio/apps/{application}/resources</c> lists every resource of
/// the folder, sorted by file, each with its <c>id</c>, <c>kind</c>, <c>name</c>, <c>file</c> and
/// <c>version</c>. <c>GET /api/studio/apps/{application}/resources/{id}</c> returns one resource with its
/// JSON <c>content</c>, and its <c>version</c> as a quoted <c>ETag</c>. The version is the SHA-256 of the
/// file's bytes. A request is checked in order: session (401), author role with Studio on (403),
/// application (404), then resource (404). The problem titles never contain text from the request.
/// Only real <c>*.json</c> files inside a listed folder are reachable.
/// </summary>
internal static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/resources", ListAsync);
        endpoints.MapGet("/api/studio/apps/{application}/resources/{id}", GetAsync);
    }

    private static async Task<IResult> ListAsync(string application, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var items = StudioFolder.List(folder)
            .Select(file => new StudioResourceItem(file.Id.ToString("D"), file.Kind, file.Name, file.File, file.Version))
            .ToList();
        return Results.Ok(new StudioResourceListResponse(items));
    }

    private static async Task<IResult> GetAsync(string application, string id, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var guid)
            || StudioFolder.List(folder).FirstOrDefault(file => file.Id == guid) is not { } found)
        {
            return NotFound("The application has no resource with this id.");
        }

        using var content = JsonDocument.Parse(found.Bytes);
        httpContext.Response.Headers.ETag = $"\"{found.Version}\"";
        return Results.Ok(new StudioResourceResponse(
            found.Id.ToString("D"), found.Kind, found.Name, found.File, found.Version, content.RootElement.Clone()));
    }

    // The checks both routes share: the folder, or the problem that refused the request.
    private static async Task<(string? Folder, IResult? Refusal)> FindFolderAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return (null, Grants.SignInRequired());
        }

        if (!options.Allows(user.Roles))
        {
            return (null, Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You do not hold the author role."));
        }

        return StudioFolder.Find(options, application) is { } folder
            ? (folder, null)
            : (null, NotFound("No Studio folder holds an application with this name."));
    }

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>The resources of a Studio folder, sorted by file.</summary>
internal sealed record StudioResourceListResponse(IReadOnlyList<StudioResourceItem> Items);

/// <summary>One resource in the list. <c>Version</c> is the lower-case SHA-256 hex of the file's bytes.</summary>
internal sealed record StudioResourceItem(string Id, string Kind, string? Name, string File, string Version);

/// <summary>One resource with its JSON content.</summary>
internal sealed record StudioResourceResponse(string Id, string Kind, string? Name, string File, string Version, JsonElement Content);
