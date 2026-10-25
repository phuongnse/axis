using System.Text.RegularExpressions;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// Every route of the server is either open or has a rate limit partition. The routes come from the server's
/// endpoint data source, so a route added later without a partition fails here.
/// </summary>
public sealed partial class RateLimitRouteTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    // The routes with no limit, as the docs list them: the site, the texts, the health probes and the SPA's pages.
    private static readonly string[] _openRoutes =
    [
        "/api/site",
        "/api/texts/{locale}",
        "/health/live",
        "/health/ready",
        "{*path:nonfile}",
    ];

    private static readonly string[] _publishingRoutes =
    [
        "/api/studio/apps/{application}/validate",
        "/api/studio/apps/{application}/publish",
        "/api/studio/apps/{application}/preview",
        "/api/studio/apps/{application}/test",
        "/api/studio/apps/{application}/releases/{id}/rollback",
    ];

    [Fact]
    public async Task The_open_routes_are_exactly_the_documented_ones()
    {
        await using var factory = CreateFactory();

        var open = Routes(factory).Where(route => route.Metadata.GetMetadata<RateLimitRoute>() is { Open: true }).Select(Pattern);

        Assert.Equal([.. _openRoutes.Order(StringComparer.Ordinal)], [.. open.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Every_other_route_has_a_partition_and_an_endpoint_route_also_has_the_bearer_partition()
    {
        await using var factory = CreateFactory();

        var routes = Routes(factory).Where(route => !_openRoutes.Contains(Pattern(route), StringComparer.Ordinal)).ToList();

        Assert.NotEmpty(routes);
        foreach (var route in routes)
        {
            var pattern = Pattern(route);
            var limit = route.Metadata.GetMetadata<RateLimitRoute>();
            Assert.True(limit is { Open: false }, $"{pattern} has no rate limit partition.");
            if (EndpointCaller.IsEndpointPath(ParameterPattern().Replace(pattern, "x")))
            {
                Assert.True(limit.Bearer, $"{pattern} takes a bearer token and has no bearer partition.");
            }
            else
            {
                Assert.NotNull(limit.Session);
            }
        }
    }

    [Fact]
    public async Task The_publishing_routes_and_sign_in_override_the_partition_of_their_group()
    {
        await using var factory = CreateFactory();

        // A pattern can be mapped for several methods, so a route is named by its method and pattern.
        var routes = Routes(factory)
            .SelectMany(route => (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => (Name: $"{method} {Pattern(route)}", Limit: route.Metadata.GetMetadata<RateLimitRoute>())))
            .ToDictionary(route => route.Name, route => route.Limit, StringComparer.Ordinal);

        Assert.All(_publishingRoutes, pattern => Assert.Equal(Partition.Publishing, routes[$"POST {pattern}"]!.Session));
        Assert.Equal(Partition.SignIn, routes["GET /api/auth/sign-in"]!.Session);
        Assert.Equal(Partition.Session, routes["GET /api/studio/apps/{application}/requests/{id}"]!.Session);
        Assert.Equal(Partition.Session, routes["POST /api/auth/sign-out"]!.Session);
        Assert.Equal(Partition.Session, routes["GET /api/me"]!.Session);
    }

    private static string Pattern(RouteEndpoint route) => route.RoutePattern.RawText ?? string.Empty;

    private static IEnumerable<RouteEndpoint> Routes(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

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

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex ParameterPattern();
}
