using System.Net;
using System.Text.RegularExpressions;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// Every route of the server is either on the open list or refuses a request without a session or a token (D111). The
/// routes come from the server's endpoint data source, so a route added later is checked without anyone adding it here.
/// </summary>
public sealed partial class AuthorizationRouteTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    // The routes that need no session, as the docs list them: the health probes, the site, the texts, sign-in, sign-out and the SPA's pages.
    private static readonly string[] _openRoutes =
    [
        "* /health/live",
        "* /health/ready",
        "GET /api/site",
        "GET /api/texts/{locale}",
        "GET /api/auth/sign-in",
        "POST /api/auth/sign-out",
        "GET {*path:nonfile}",
        "HEAD {*path:nonfile}",
    ];

    [Fact]
    public async Task The_open_routes_are_exactly_the_documented_ones()
    {
        await using var factory = CreateFactory();

        var open = Routes(factory)
            .Where(route => route.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(Names);

        Assert.Equal([.. _openRoutes.Order(StringComparer.Ordinal)], [.. open.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Every_other_route_refuses_a_request_without_a_session_or_a_token()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var routes = Routes(factory)
            .SelectMany(route => Names(route).Select(name => (Name: name, Route: route)))
            .Where(entry => !_openRoutes.Contains(entry.Name, StringComparer.Ordinal))
            .ToList();
        Assert.NotEmpty(routes);

        // The tenant database is unreachable, so a route that reached it would answer 500.
        var failures = new List<string>();
        foreach (var (name, route) in routes)
        {
            var method = name[..name.IndexOf(' ', StringComparison.Ordinal)];
            var path = ParameterPattern().Replace(route.RoutePattern.RawText ?? string.Empty, "x");
            path = path.StartsWith('/') ? path : "/" + path;
            using var request = new HttpRequestMessage(new HttpMethod(method == "*" ? "GET" : method), path);
            request.Headers.Add("X-Axis-Csrf", "1");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                && contentType == "application/problem+json";
            if (EndpointCaller.IsEndpointPath(path))
            {
                refused = refused
                    && response.StatusCode == HttpStatusCode.Unauthorized
                    && response.Headers.WwwAuthenticate.ToString().StartsWith("Bearer", StringComparison.Ordinal);
            }

            if (!refused)
            {
                failures.Add($"{name} -> {(int)response.StatusCode} {contentType}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // A pattern can be mapped for several methods, so a route is named by its method and pattern.
    private static IEnumerable<string> Names(RouteEndpoint route) =>
        (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
            .Select(method => $"{method} {route.RoutePattern.RawText}");

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
