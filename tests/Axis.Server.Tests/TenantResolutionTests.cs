using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class TenantResolutionTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string UnknownHost = "unknown.example:8443";

    private static readonly string[] _defaultTenant =
    [
        "Tenants:default:Hosts:0=localhost",
        $"Tenants:default:ConnectionString={UnreachableDatabase}",
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string[], string> InvalidTenants => new()
    {
        { [], "No tenants are configured" },
        { ["Tenants:Default:Hosts:0=localhost", $"Tenants:Default:ConnectionString={UnreachableDatabase}"], "Tenant id 'Default' is invalid" },
        { ["Tenants:tenant_a:Hosts:0=localhost", $"Tenants:tenant_a:ConnectionString={UnreachableDatabase}"], "Tenant id 'tenant_a' is invalid" },
        { [$"Tenants:default:ConnectionString={UnreachableDatabase}"], "Tenant 'default' has no hosts." },
        { ["Tenants:default:Hosts:0=", $"Tenants:default:ConnectionString={UnreachableDatabase}"], "Tenant 'default' has a blank host." },
        { ["Tenants:default:Hosts:0=localhost"], "Tenant 'default' has no connection string." },
        { ["Tenants:default:Hosts:0=localhost", "Tenants:default:ConnectionString= "], "Tenant 'default' has no connection string." },
        {
            [
                "Tenants:a:Hosts:0=shared.example.test", $"Tenants:a:ConnectionString={UnreachableDatabase}",
                "Tenants:b:Hosts:0=SHARED.example.test", $"Tenants:b:ConnectionString={UnreachableDatabase}",
            ],
            "Host 'SHARED.example.test' is configured for both tenant 'a' and tenant 'b'."
        },
    };

    [Theory]
    [MemberData(nameof(InvalidTenants))]
    public async Task Startup_fails_with_a_message_naming_the_tenant_configuration_problem(string[] settings, string problem)
    {
        await using var factory = CreateFactory(settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(problem, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/anything")]
    [InlineData("/apps/x/page")]
    [InlineData("/")]
    public async Task Unknown_host_gets_a_404_problem_that_names_no_tenant_or_host(string path)
    {
        await using var factory = CreateFactory(_defaultTenant);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get(path, UnknownHost), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("No tenant is configured for this host.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unknown.example", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LocalHost:8443")]
    public async Task Known_host_reaches_the_app_ignoring_case_and_port(string host)
    {
        await using var factory = CreateFactory(_defaultTenant);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/apps/x/page", host), CancellationToken);

        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Liveness_returns_200_for_an_unknown_host()
    {
        await using var factory = CreateFactory(_defaultTenant);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/health/live", UnknownHost), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_not_tenant_resolved_for_an_unknown_host()
    {
        await using var factory = CreateFactory(_defaultTenant);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/health/ready", UnknownHost), CancellationToken);

        // The database is unreachable here; the integration tests show 200 with a reachable one.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpRequestMessage Get(string path, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(string[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            foreach (var setting in settings)
            {
                var separator = setting.IndexOf('=', StringComparison.Ordinal);
                builder.UseSetting(setting[..separator], setting[(separator + 1)..]);
            }
        });
}
