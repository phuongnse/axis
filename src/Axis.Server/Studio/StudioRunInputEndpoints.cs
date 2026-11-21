using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Server.Tenancy;
using Axis.Studio;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// The routes of a package's saved run inputs, which the application's developers share, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. <c>GET /api/studio/apps/{application}/packages/{package}/run-inputs</c>
/// answers <c>{ items: [{ name, operation, input }] }</c> with the version as a quoted <c>ETag</c>. Before the first save it answers
/// <c>{ items: [] }</c> with no <c>ETag</c>, so the client sends <c>If-None-Match: *</c> to create. <c>PUT</c> replaces all the items
/// with <c>If-Match</c> or <c>If-None-Match: *</c> and answers <c>{ version }</c>. Both routes need the author role, the developer role
/// and the tenant's application packages switch. They are checked in order: session (401), author role (403), developer role (403), the
/// switch (403), the application (404), the package, which must be a plain folder name (404 for <c>GET</c>, 400 for <c>PUT</c>), and for
/// <c>PUT</c> the precondition headers (428 for none, 400 for both), the content type (415), the body (400), an <c>If-None-Match</c> other
/// than <c>*</c> (409), then the write lock (503) and the version (409). In a cell the <c>PUT</c> checks the tenant's storage quota
/// (403 <c>quota-exceeded</c>) right after the session. The inputs are the file <c>run-inputs/&lt;package&gt;.json</c>, which the loader
/// skips, and each save is one revision.
/// </summary>
internal static class StudioRunInputEndpoints
{
    internal const string BadPackage = "The package is not a plain folder name.";

    internal const string StaleRunInputs = "The run inputs have changed since this version, or they exist already. Read them again.";

    internal const string BadBody =
        "The body must be an object with only items, a list of objects with a non-empty string name and operation and an input, and no name twice.";

    public static void MapStudioRunInputEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/packages/{package}/run-inputs", GetAsync);
        endpoints.MapPut("/api/studio/apps/{application}/packages/{package}/run-inputs", PutAsync).WithStorageQuota();
    }

    private static async Task<IResult> GetAsync(string application, string package, HttpContext httpContext, StudioOptions options)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, developer: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioRunInputs.File(package) is not { } file)
        {
            return StudioEndpoints.NotFound(BadPackage);
        }

        if (StudioPackageFiles.Read(folder, file) is not { } bytes)
        {
            return Results.Ok(new StudioRunInputsResponse([]));
        }

        httpContext.Response.Headers.ETag = $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))}\"";
        return Results.Ok(new StudioRunInputsResponse(Items(bytes)));
    }

    private static async Task<IResult> PutAsync(
        string application, string package, HttpContext httpContext, StudioOptions options, IStudioWriteLock writeLock)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, developer: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioRunInputs.File(package) is null)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadPackage);
        }

        var request = httpContext.Request;
        var hasIfMatch = request.Headers.IfMatch.Count > 0;
        var hasIfNoneMatch = request.Headers.IfNoneMatch.Count > 0;
        if (!hasIfMatch && !hasIfNoneMatch)
        {
            return StudioEndpoints.Problem(
                StatusCodes.Status428PreconditionRequired,
                "The save needs the inputs' version in If-Match, or If-None-Match: * when none are saved.");
        }

        if (hasIfMatch && hasIfNoneMatch)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "Send If-Match or If-None-Match, not both.");
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        byte[] bytes;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
            bytes = StudioWrites.Serialize(new JsonObject { ["items"] = ValidItems(body).DeepClone() });
        }
        catch (JsonException)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadBody);
        }

        // A tag other than * in If-None-Match matches no state of the inputs, so the save is stale.
        if (hasIfNoneMatch && request.Headers.IfNoneMatch.ToString() != "*")
        {
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, StaleRunInputs);
        }

        var outcome = await StudioWrites.WriteRunInputsAsync(
            writeLock,
            await StudioEndpoints.RevisionsAsync(httpContext, options, folder),
            folder,
            package,
            bytes,
            hasIfMatch ? request.Headers.IfMatch.ToString() : null,
            hasIfNoneMatch,
            httpContext.RequestAborted);
        if (outcome.Refusal is { } refused)
        {
            return refused switch
            {
                StudioWriteRefusal.Unavailable => StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable),
                StudioWriteRefusal.Stale or StudioWriteRefusal.Exists => StudioEndpoints.Problem(StatusCodes.Status409Conflict, StaleRunInputs),
                _ => StudioEndpoints.Problem(StatusCodes.Status400BadRequest, BadPackage),
            };
        }

        httpContext.Response.Headers.ETag = $"\"{outcome.Version}\"";
        return Results.Ok(new StudioVersionResponse(outcome.Version!));
    }

    // The items array of a body that is an object with only items, each item an object with exactly a non-empty string name and
    // operation and an input, and no name twice. Anything else is a JsonException.
    private static JsonArray ValidItems(JsonObject body)
    {
        if (body.Count != 1 || body["items"] is not JsonArray items)
        {
            throw new JsonException();
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in items)
        {
            if (node is not JsonObject item
                || item.Count != 3
                || !item.ContainsKey("input")
                || NonEmptyString(item["name"]) is not { } name
                || NonEmptyString(item["operation"]) is null
                || !names.Add(name))
            {
                throw new JsonException();
            }
        }

        return items;
    }

    private static string? NonEmptyString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

    // The items of the saved bytes. Bytes that are not an object with an items array hold none, so a save replaces them.
    private static List<StudioRunInput> Items(byte[] bytes)
    {
        var items = new List<StudioRunInput>();
        try
        {
            if (JsonNode.Parse(bytes) is JsonObject { } root && root["items"] is JsonArray array)
            {
                foreach (var node in array)
                {
                    if (node is JsonObject item && NonEmptyString(item["name"]) is { } name && NonEmptyString(item["operation"]) is { } operation)
                    {
                        items.Add(new StudioRunInput(name, operation, item["input"]?.DeepClone()));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Bytes that are not JSON hold no items.
        }

        return items;
    }
}

/// <summary>A package's saved run inputs, in the order they were saved.</summary>
internal sealed record StudioRunInputsResponse(IReadOnlyList<StudioRunInput> Items);

/// <summary>One saved run input: its name, the operation it is for and its input, which may be any JSON value.</summary>
internal sealed record StudioRunInput(string Name, string Operation, JsonNode? Input);

/// <summary>The version of what a save wrote, the SHA-256 of its bytes as lower-case hex.</summary>
internal sealed record StudioVersionResponse(string Version);
