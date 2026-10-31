using Axis.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// The operator console only reads, apart from the operator commands: discard and redeliver a dead letter, and retry,
/// cancel and resolve on an instance. The routes come from the server's endpoint
/// data source, so a route added to the console later is checked without anyone adding it here.
/// </summary>
public sealed class ConsoleRouteTests
{
    // Port 1 on loopback refuses connections immediately, and these tests never reach the database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static readonly string[] _writeRoutes =
    [
        "POST /api/console/dead-letters/{id}/discard",
        "POST /api/console/dead-letters/{id}/redeliver",
        "POST /api/console/instances/{id}/calls/{callId}/resolve",
        "POST /api/console/instances/{id}/cancel",
        "POST /api/console/instances/{id}/retry",
    ];

    [Fact]
    public async Task The_console_has_no_write_route_except_the_operator_commands()
    {
        await using var factory = CreateFactory();

        var writes = ConsoleRoutes(factory).Where(name => !name.StartsWith("GET ", StringComparison.Ordinal));

        Assert.Equal(_writeRoutes, writes.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task The_console_has_no_route_that_publishes_activates_or_rolls_back()
    {
        await using var factory = CreateFactory();

        var routes = ConsoleRoutes(factory).ToList();

        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, name => name.Contains("rollback", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, name => name.Contains("activate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            routes.Where(name => !name.StartsWith("GET ", StringComparison.Ordinal)),
            name => name.Contains("publish", StringComparison.OrdinalIgnoreCase) || name.Contains("release", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_console_reads_the_publishing_of_an_application()
    {
        await using var factory = CreateFactory();

        Assert.Contains("GET /api/console/applications/{id}/publishing", ConsoleRoutes(factory));
    }

    private static IEnumerable<string> ConsoleRoutes(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText?.StartsWith("/api/console", StringComparison.Ordinal) == true)
            .SelectMany(route => (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {route.RoutePattern.RawText}"));

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
        });
}
