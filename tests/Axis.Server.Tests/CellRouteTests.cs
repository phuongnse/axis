using Axis.Server.Authentication;
using Axis.Server.CellApi;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Axis.Server.Tests;

/// <summary>
/// Every route under <c>/api/cell/</c> is a route of the provisioning API: it carries the operator route marker and
/// the cell operator policy, and it is never open. The two exceptions are the console's sign-in and sign-out, which carry the
/// marker and are open, because they must work for a caller who has no session. The routes come from the server's endpoint data source, so a route added
/// to the group later is checked without anyone adding it here. A cell with a console host also needs the address of the
/// identity provider at start.
/// </summary>
public sealed class CellRouteTests
{
    // Port 1 on loopback refuses connections immediately, and these tests never reach the database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static readonly string[] _sessionRoutes = ["GET /api/cell/auth/sign-in", "POST /api/cell/auth/sign-out"];

    [Fact]
    public async Task Every_route_under_api_cell_but_sign_in_and_sign_out_needs_the_operator_policy_and_is_never_open()
    {
        await using var factory = CreateFactory();

        var routes = CellRoutes(factory).Where(route => !Names(route).Intersect(_sessionRoutes).Any()).ToList();

        Assert.NotEmpty(routes);
        Assert.Contains(routes, route => Names(route).Contains("GET /api/cell/session"));
        Assert.All(routes, route =>
        {
            var name = route.RoutePattern.RawText;
            Assert.True(route.Metadata.GetMetadata<CellOperatorRoute>() is not null, $"{name} has no operator route marker.");
            Assert.Contains(
                route.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => data.Policy == CellOperator.Policy);
            Assert.True(route.Metadata.GetMetadata<IAllowAnonymous>() is null, $"{name} allows anonymous access.");
        });
    }

    [Fact]
    public async Task Sign_in_and_sign_out_carry_the_route_marker_and_skip_the_operator_policy()
    {
        await using var factory = CreateFactory();

        var routes = CellRoutes(factory).Where(route => Names(route).Intersect(_sessionRoutes).Any()).ToList();

        Assert.Equal(2, routes.Count);
        Assert.All(routes, route =>
        {
            var name = route.RoutePattern.RawText;
            Assert.True(route.Metadata.GetMetadata<CellOperatorRoute>() is not null, $"{name} has no operator route marker.");
            Assert.True(route.Metadata.GetMetadata<IAllowAnonymous>() is not null, $"{name} needs a session.");
            Assert.DoesNotContain(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), data => data.Policy == CellOperator.Policy);
        });
    }

    [Fact]
    public async Task The_provisioning_API_reads_the_cell_the_tenants_jobs_and_exports_sets_quotas_queues_create_suspend_resume_delete_and_cancel_deletion_takes_host_requests_and_has_the_session_routes_sign_in_and_out_and_the_session_read()
    {
        await using var factory = CreateFactory();

        var names = CellRoutes(factory).SelectMany(Names).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "DELETE /api/cell/tenants/{id}",
                "GET /api/cell/",
                "GET /api/cell/auth/sign-in",
                "GET /api/cell/jobs",
                "GET /api/cell/jobs/{id:guid}",
                "GET /api/cell/session",
                "GET /api/cell/tenants",
                "GET /api/cell/tenants/{id}",
                "GET /api/cell/tenants/{id}/exports/{exportId:guid}",
                "POST /api/cell/auth/sign-out",
                "POST /api/cell/tenants",
                "POST /api/cell/tenants/{id}/deletion/cancel",
                "POST /api/cell/tenants/{id}/exports",
                "POST /api/cell/tenants/{id}/hosts",
                "POST /api/cell/tenants/{id}/resume",
                "POST /api/cell/tenants/{id}/suspend",
                "PUT /api/cell/tenants/{id}/quotas",
            ],
            names);
    }

    [Fact]
    public async Task A_cell_with_a_console_host_and_no_identity_provider_address_stops_at_start_naming_the_setting()
    {
        await using var factory = CreateFactory(
            ("Tenancy:Source", "directory"),
            ("Cell:Id", "vn-1"),
            ("Cell:Region", "vn"),
            ("Cell:TenantDomain", "vn.example.test"),
            ("Cell:ConsoleHost", "console.test"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("IdentityProvider:Url", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_without_a_console_or_internal_host_needs_no_identity_provider_address()
    {
        var cell = new CellOptions { Id = "vn-1", Region = "vn", TenantDomain = "vn.example.test" };
        var configuration = new ConfigurationBuilder().Build();

        var authority = CellOperator.Load(configuration, cell, new HostingEnvironment());

        Assert.Null(authority);
    }

    [Fact]
    public void The_operator_realm_is_found_under_the_identity_provider_address()
    {
        var cell = new CellOptions
        {
            Id = "vn-1",
            Region = "vn",
            TenantDomain = "vn.example.test",
            ConsoleHost = "console.test",
            OperatorRealm = "ops",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["IdentityProvider:Url"] = "https://id.example.test/auth" })
            .Build();

        var authority = CellOperator.Load(configuration, cell, new HostingEnvironment { EnvironmentName = Environments.Production });

        Assert.Equal(new Uri("https://id.example.test/auth/realms/ops"), authority);
    }

    private static IEnumerable<string> Names(RouteEndpoint route) =>
        (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
            .Select(method => $"{method} {route.RoutePattern.RawText}");

    private static IEnumerable<RouteEndpoint> CellRoutes(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText?.StartsWith("/api/cell", StringComparison.Ordinal) == true);

    private static WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
        });
}
