using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;

namespace Axis.Server.Processes;

/// <summary>
/// The routes of a customer-hosted package runner, see <c>docs/reference/extension-packages.md#routes</c>. A request is checked in
/// order: the tenant of the host (<c>404</c>), the runner's token and row (<c>401</c>, see <see cref="PackageRunnerCaller"/>), the body
/// (<c>400</c>), then the releases (<c>409</c>).
/// <para>
/// <c>POST /api/package-runner/packages</c> checks each registered manifest with <see cref="RegisteredManifestCheck"/>. When every manifest
/// is equal to a pin, one statement adds the packages to the runner's stored list without duplicates and the answer is <c>204</c>. A manifest
/// that differs from a pin, or that no release in use pins as customer-hosted, answers <c>409</c> and stores nothing. The titles name the
/// package and the version, and no release or application. A runner that is revoked while the request runs answers <c>401</c> and stores
/// nothing.
/// </para>
/// </summary>
internal static class PackageRunnerEndpoints
{
    public static void MapPackageRunnerEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/package-runner/packages", RegisterAsync);

    private static async Task<IResult> RegisterAsync(
        HttpContext context, TenantDatabase database, PackageCatalog packages, CancellationToken cancellationToken)
    {
        using var body = await ReadBodyAsync(context.Request, cancellationToken);
        if (body is null || ReadManifests(body.RootElement) is not { } manifests)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The body needs a list of packages, each with a manifest that has an id and a version.");
        }

        var runnerId = Guid.Parse(EndpointCaller.Read(context.User)!.Id);
        var connection = await database.GetConnectionAsync(cancellationToken);

        // The check throws when a release in use no longer compiles. That is a server fault, so it is not caught here.
        foreach (var (id, version, manifest) in manifests)
        {
            switch (await RegisteredManifestCheck.CheckAsync(connection, manifest, packages, cancellationToken))
            {
                case RegisteredManifestOutcome.Different:
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"The manifest of package '{id}' version {version} differs from the one a release in use pins.");
                case RegisteredManifestOutcome.NotPinned:
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"No release in use pins package '{id}' version {version} as customer-hosted.");
            }
        }

        var added = manifests.Select(manifest => new PackageRunnerPackage(manifest.Id, manifest.Version)).Distinct().ToList();
        return await PackageRunners.AddPackagesAsync(connection, runnerId, added, cancellationToken)
            ? Results.NoContent()
            : Grants.PackageRunnerTokenRequired(context);
    }

    /// <summary>The parsed body, or null when it is not JSON.</summary>
    private static async Task<JsonDocument?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The manifests of <c>{ "packages": [{ "manifest": {...} }] }</c>, or null when the shape is wrong: the list must not be empty, and
    /// each manifest must be an object with a non-empty <c>id</c> and <c>version</c>, which are strings.
    /// </summary>
    private static List<(string Id, string Version, JsonElement Manifest)>? ReadManifests(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("packages", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() == 0)
        {
            return null;
        }

        var manifests = new List<(string, string, JsonElement)>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("manifest", out var manifest)
                || !TryReadText(manifest, "id", out var id)
                || !TryReadText(manifest, "version", out var version))
            {
                return null;
            }

            manifests.Add((id, version, manifest));
        }

        return manifests;
    }

    private static bool TryReadText(JsonElement manifest, string property, out string value)
    {
        value = string.Empty;
        if (manifest.ValueKind == JsonValueKind.Object
            && manifest.TryGetProperty(property, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        return false;
    }
}
