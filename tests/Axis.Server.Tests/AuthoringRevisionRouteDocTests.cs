using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// The route table of <c>docs/reference/authoring-revisions.md</c> lists exactly the revision and draft routes the server
/// maps, so a route added to either side without the other fails here.
/// </summary>
public sealed partial class AuthoringRevisionRouteDocTests
{
    private const string Prefix = "/api/studio/apps/{application}/";

    // Port 1 on loopback refuses connections immediately. The test reads the route table and never calls a route.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [Fact]
    public async Task The_route_table_of_the_doc_lists_the_mapped_revision_and_draft_routes()
    {
        var documented = DocumentedRoutes();
        Assert.NotEmpty(documented);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
        });
        var mapped = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText is { } pattern
                && (pattern.StartsWith(Prefix + "revisions", StringComparison.Ordinal) || pattern.StartsWith(Prefix + "drafts", StringComparison.Ordinal)))
            .SelectMany(route => (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {route.RoutePattern.RawText}"));

        Assert.Equal([.. mapped.Order(StringComparer.Ordinal)], [.. documented.Order(StringComparer.Ordinal)]);
    }

    private static List<string> DocumentedRoutes()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "reference", "authoring-revisions.md");
        return [.. File.ReadAllLines(path).Select(line => RouteRow().Match(line)).Where(match => match.Success).Select(match => match.Groups[1].Value)];
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

    // A table row that starts with a route in backticks, such as "| `GET /api/studio/apps/{application}/revisions` |".
    [GeneratedRegex(@"^\| `((?:GET|POST|PUT|PATCH|DELETE) /api/[^`]*)`")]
    private static partial Regex RouteRow();
}
