using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// The <c>Route</c> tables under <c>docs/</c> list exactly the Studio, console, cell, tenant export and tenant usage routes
/// the server maps, so a route added or removed on one side without the other fails here.
/// </summary>
public sealed partial class RouteTableDocTests
{
    private static readonly string[] _prefixes = ["/api/studio", "/api/console", "/api/cell", "/api/tenant/exports", "/api/tenant/usage"];

    // Port 1 on loopback refuses connections immediately. The test reads the route table and never calls a route.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [Fact]
    public async Task The_route_tables_of_the_docs_list_the_mapped_Studio_console_and_cell_routes()
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
            .SelectMany(route => (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Method: method, Pattern: route.RoutePattern.RawText ?? string.Empty)))
            .Where(route => InScope(route.Pattern))
            // The empty route of a group ends with a slash in its pattern, and the docs write it without one.
            .Select(route => $"{route.Method} {RouteConstraint().Replace(route.Pattern, "{$1}").TrimEnd('/')}")
            .Distinct()
            .ToList();

        Assert.Equal([.. mapped.Order(StringComparer.Ordinal)], [.. documented.Distinct().Order(StringComparer.Ordinal)]);
    }

    private static List<string> DocumentedRoutes()
    {
        var documented = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "docs"), "*.md", SearchOption.AllDirectories))
        {
            var inRouteTable = false;
            var previous = string.Empty;
            foreach (var line in File.ReadLines(file))
            {
                if (!line.StartsWith('|'))
                {
                    inRouteTable = false;
                }
                else if (!previous.StartsWith('|'))
                {
                    inRouteTable = RouteHeader().IsMatch(line);
                }
                else if (inRouteTable && !line.Contains("_(planned for ", StringComparison.Ordinal)
                    && RouteRow().Match(line) is { Success: true } row && InScope(row.Groups["path"].Value))
                {
                    documented.Add($"{row.Groups["method"].Value} {row.Groups["path"].Value}");
                }

                previous = line;
            }
        }

        return documented;
    }

    private static bool InScope(string pattern) =>
        _prefixes.Any(prefix => pattern == prefix || pattern.StartsWith(prefix + "/", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    [GeneratedRegex(@"^\| Route\s*\|")]
    private static partial Regex RouteHeader();

    // A row of a Route table that starts with a route in backticks, such as "| `GET /api/studio/catalog` |".
    [GeneratedRegex(@"^\| `(?<method>GET|POST|PUT|PATCH|DELETE) (?<path>/api/[^`]*)`")]
    private static partial Regex RouteRow();

    // A route constraint such as {exportId:guid} is written {exportId} in the docs.
    [GeneratedRegex(@"\{(\w+):\w+\}")]
    private static partial Regex RouteConstraint();
}
