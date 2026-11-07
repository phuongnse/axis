using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// The OpenAPI document that the build writes to <c>artifacts/openapi/axis-server.json</c> is an OpenAPI 3.1 document and lists
/// exactly the method and path of each route in the server's endpoint data source, bar the endpoints named here with a reason.
/// A route added or removed without the document following it fails here. See <c>docs/development.md#build</c>.
/// </summary>
public sealed partial class OpenApiDocumentTests
{
    // The endpoints that the document does not describe, each with the reason. A pair that is not mapped any more fails the test.
    private static readonly Dictionary<string, string> _leftOut = new(StringComparer.Ordinal)
    {
        ["* /health/live"] = "A health check pipeline answers it. It has no handler method to describe and no API caller.",
        ["* /health/ready"] = "A health check pipeline answers it. It has no handler method to describe and no API caller.",
        ["GET /api/{path}"] = "The 404 fallback of the API. It answers a path that no route maps and is not a route of the API.",
        ["HEAD /api/{path}"] = "The 404 fallback of the API. It answers a path that no route maps and is not a route of the API.",
        ["GET /{path}"] = "The SPA fallback. It serves index.html for the console's pages and is not a route of the API.",
        ["HEAD /{path}"] = "The SPA fallback. It serves index.html for the console's pages and is not a route of the API.",
    };

    [Fact]
    public void The_document_is_an_OpenAPI_3_1_document()
    {
        using var document = ReadDocument();

        Assert.StartsWith("3.1.", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_document_lists_the_method_and_path_of_each_mapped_route_but_the_left_out_endpoints()
    {
        using var document = ReadDocument();
        var described = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        await using var factory = OpenApiHost.Create();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(route => (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {OpenApiPath(route.RoutePattern.RawText ?? string.Empty)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(_leftOut.Keys, name => Assert.True(routes.Contains(name), $"{name} is left out of the document but is not mapped."));

        var expected = routes.Except(_leftOut.Keys).ToHashSet(StringComparer.Ordinal);
        var differences = expected.Except(described).Order(StringComparer.Ordinal).Select(name => $"{name} is mapped but not in the document")
            .Concat(described.Except(expected).Order(StringComparer.Ordinal).Select(name => $"{name} is in the document but not mapped"))
            .ToList();
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    /// <summary>Writes a route pattern the way OpenAPI writes a path: parameters have only their name, with no constraint, default, catch-all marker or optional marker, and a path has no trailing slash.</summary>
    private static string OpenApiPath(string pattern) => "/" + ParameterPattern().Replace(pattern.Trim('/'), "{$1}");

    private static JsonDocument ReadDocument()
    {
        var path = Path.Combine(RepositoryRoot(), OpenApiHost.DocumentPath);
        Assert.True(File.Exists(path), $"{OpenApiHost.DocumentPath} is missing. Run: dotnet run --project tests/Axis.OpenApiDocument -c Release");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    [GeneratedRegex(@"\{\*{0,2}([^:}=?]+)[^}]*\}")]
    private static partial Regex ParameterPattern();
}
