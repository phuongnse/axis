using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// Every route of the server that can write is either checked against the tenant's storage quota or on the exempt list.
/// The routes come from the server's endpoint data source, so a route added later with neither fails here. See
/// <c>docs/reference/tenant-quotas.md#refusals</c>.
/// </summary>
public sealed class StorageQuotaRouteTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static readonly string[] _covered =
    [
        "POST /api/apps/{app}/endpoints/{name}",
        "POST /api/apps/{app}/entities/{entity}/fields/{collection}/{field}/files",
        "POST /api/apps/{app}/entities/{entity}/fields/{field}/files",
        "POST /api/apps/{app}/entities/{entity}/imports",
        "POST /api/apps/{app}/entities/{entity}/imports/{id}/commit",
        "POST /api/apps/{app}/entities/{entity}/records",
        "POST /api/apps/{app}/entities/{module}/{entity}/imports",
        "POST /api/apps/{app}/entities/{module}/{entity}/imports/{id}/commit",
        "POST /api/apps/{app}/entities/{module}/{entity}/records",
        "PATCH /api/apps/{app}/entities/{entity}/records/{id}",
        "PATCH /api/apps/{app}/entities/{module}/{entity}/records/{id}",
        "PATCH /api/studio/apps/{application}/resources/{id}",
        "POST /api/studio/apps",
        "POST /api/studio/apps/{application}/changes",
        "POST /api/studio/apps/{application}/publish",
        "POST /api/studio/apps/{application}/resources",
        "PUT /api/studio/apps/{application}/modules/{module}/resources/{id}",
        "PUT /api/studio/apps/{application}/texts/{key}",
    ];

    // Routes that can write and are not refused over the quota on purpose. Deletes, process starts and task work go on,
    // see docs/reference/tenant-quotas.md#refusals. A new route does not belong here unless the docs say it goes on.
    private static readonly HashSet<string> _exempt = new(StringComparer.Ordinal)
    {
        // The health probes answer every method and write nothing.
        "* /health/live",
        "* /health/ready",

        // Deletes are never refused over the quota.
        "DELETE /api/apps/{app}/entities/{entity}/records/{id}",
        "DELETE /api/apps/{app}/entities/{module}/{entity}/records/{id}",

        // Sessions, sign-out and form state hold no tenant data that the quota counts.
        "DELETE /api/me/sessions",
        "DELETE /api/me/sessions/{id}",
        "POST /api/auth/sign-out",
        "POST /api/apps/{app}/forms/{form}/state",
        "POST /api/apps/{app}/tasks/{id}/form-state",

        // Notifications.
        "POST /api/apps/{app}/notifications/read-all",
        "POST /api/apps/{app}/notifications/{id}/read",

        // A process start writes its instance, and task and instance work goes on over the quota.
        "POST /api/apps/{app}/processes/{module}/{process}/instances",
        "POST /api/apps/{app}/processes/{process}/instances",
        "POST /api/apps/{app}/tasks/{id}/claim",
        "POST /api/apps/{app}/tasks/{id}/complete",
        "POST /api/apps/{app}/tasks/{id}/reassign",
        "POST /api/apps/{app}/tasks/{id}/release",

        // The process operator's dead letters and instances, and the console.
        "POST /api/apps/{app}/processes/{process}/dead-letters/{id}/discard",
        "POST /api/apps/{app}/processes/{process}/dead-letters/{id}/redeliver",
        "POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve",
        "POST /api/apps/{app}/processes/{process}/instances/{id}/cancel",
        "POST /api/apps/{app}/processes/{process}/instances/{id}/retry",
        "POST /api/console/dead-letters/{id}/discard",
        "POST /api/console/dead-letters/{id}/redeliver",
        "POST /api/console/instances/{id}/calls/{callId}/resolve",
        "POST /api/console/instances/{id}/cancel",
        "POST /api/console/instances/{id}/retry",

        // User commands.
        "POST /api/users/invite",
        "POST /api/users/{id}/disable",
        "POST /api/users/{id}/enable",
        "POST /api/users/{id}/give-role",
        "POST /api/users/{id}/remove-role",
        "POST /api/users/{id}/resend-invitation",
        "POST /api/users/{id}/reset-authenticator",

        // Studio deletes are never refused.
        "DELETE /api/studio/apps/{application}/resources/{id}",

        // Validate, preview, test and rollback store a request and go on over the quota.
        "POST /api/studio/apps/{application}/preview",
        "POST /api/studio/apps/{application}/releases/{id}/rollback",
        "POST /api/studio/apps/{application}/test",
        "POST /api/studio/apps/{application}/validate",

        // The expression check and the page metadata compile in memory and write nothing.
        "POST /api/studio/apps/{application}/expressions/check",
        "POST /api/studio/apps/{application}/resources/{id}/page-metadata",
    };

    [Fact]
    public async Task Every_route_that_can_write_is_checked_against_the_storage_quota_or_exempt()
    {
        await using var factory = CreateFactory();

        var uncovered = Uncovered(Routes(factory), _exempt);

        Assert.True(uncovered.Count == 0, $"These routes write and are neither checked against the storage quota nor exempt:{Environment.NewLine}{string.Join(Environment.NewLine, uncovered.Select(name => $"        \"{name}\","))}");
    }

    [Fact]
    public async Task The_checked_routes_are_exactly_the_documented_ones_and_no_exempt_route_is_checked()
    {
        await using var factory = CreateFactory();

        var routes = Names(Routes(factory)).ToList();
        var checkedRoutes = routes.Where(route => HasCheck(route.Endpoint)).Select(route => route.Name);

        Assert.Equal([.. _covered.Order(StringComparer.Ordinal)], [.. checkedRoutes.Order(StringComparer.Ordinal)]);
        Assert.DoesNotContain(routes, route => HasCheck(route.Endpoint) && _exempt.Contains(route.Name));
        Assert.All(_exempt, name => Assert.Contains(routes, route => route.Name == name));
    }

    [Fact]
    public void A_write_route_with_no_check_that_is_not_exempt_is_reported()
    {
        var unchecked_ = Endpoint("/api/x", ["POST"]);
        var checkedRoute = Endpoint("/api/y", ["POST"], new StorageQuotaMetadata());
        var exempt = Endpoint("/api/z", ["DELETE"]);
        var read = Endpoint("/api/r", ["GET", "HEAD"]);
        var fallback = Endpoint("{*path:nonfile}", []);

        var uncovered = Uncovered([unchecked_, checkedRoute, exempt, read, fallback], new HashSet<string>(StringComparer.Ordinal) { "DELETE /api/z" });

        Assert.Equal(["* {*path:nonfile}", "POST /api/x"], [.. uncovered.Order(StringComparer.Ordinal)]);
    }

    // The names of the routes that can write and have neither the check nor an entry in the exempt list.
    private static List<string> Uncovered(IEnumerable<RouteEndpoint> routes, HashSet<string> exempt) =>
        [.. Names(routes)
            .Where(route => !HttpMethods.IsGet(route.Method) && !HttpMethods.IsHead(route.Method))
            .Where(route => !HasCheck(route.Endpoint) && !exempt.Contains(route.Name))
            .Select(route => route.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    // A pattern can be mapped for several methods, so a route is named by its method and pattern. An endpoint with no
    // method metadata answers every method, which is the SPA fallback.
    private static IEnumerable<(string Name, string Method, RouteEndpoint Endpoint)> Names(IEnumerable<RouteEndpoint> routes) =>
        routes.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is { Count: > 0 } declared ? declared : ["*"])
                .Select(method => (Name: $"{method} {endpoint.RoutePattern.RawText?.TrimEnd('/')}", Method: method, Endpoint: endpoint)));

    private static bool HasCheck(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<StorageQuotaMetadata>() is not null;

    private static RouteEndpoint Endpoint(string pattern, string[] methods, params object[] metadata)
    {
        var all = new List<object>(metadata);
        if (methods.Length > 0)
        {
            all.Add(new HttpMethodMetadata(methods));
        }

        return new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(all), pattern);
    }

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
}
