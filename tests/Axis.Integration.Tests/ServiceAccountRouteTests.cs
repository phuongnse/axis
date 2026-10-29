using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Axis.Server.Authentication;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// A service account's bearer token reaches only the routes marked with <see cref="ServiceAccountRoute"/>, the record,
/// data source rows, process start, process inspection and operator command routes, the file upload and download routes, and the endpoint routes, which take their own token. The test reads every <c>/api</c> route from the
/// server's endpoint data source, so a new route that accepts the token by mistake fails it. The database is
/// unreachable, so a request that reaches it fails the test.
/// </summary>
public sealed class ServiceAccountRouteTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string Host = "localhost";

    // Routes that answer a request without any credentials. A new route that does is a test failure until it is reviewed
    // and named here.
    private static readonly string[] _anonymousRoutes =
    [
        "GET /api/auth/sign-in",
        "GET /api/site",
        "GET /api/texts/{locale}",
        "POST /api/auth/sign-out",
    ];

    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public void The_routes_that_carry_the_marker_are_exactly_the_service_account_routes()
    {
        var marked = Routes().Where(route => route.Endpoint.Metadata.GetMetadata<ServiceAccountRoute>() is not null)
            .Select(route => route.Name)
            .Order(StringComparer.Ordinal);

        string[] patterns = ["/api/apps/{app}/entities/{entity}/records", "/api/apps/{app}/entities/{module}/{entity}/records"];
        var expected = patterns
            .SelectMany(pattern => new[]
            {
                $"GET {pattern}",
                $"GET {pattern}/{{id}}",
                $"GET {pattern}/{{id}}/history",
                $"POST {pattern}",
                $"PATCH {pattern}/{{id}}",
                $"DELETE {pattern}/{{id}}",
            })
            .Concat(
            [
                "GET /api/apps/{app}/data-sources/{dataSource}/rows",
                "GET /api/apps/{app}/data-sources/{module}/{dataSource}/rows",
                "POST /api/apps/{app}/processes/{process}/instances",
                "GET /api/apps/{app}/processes/{process}/instances",
                "GET /api/apps/{app}/processes/{process}/instances/{id}",
                "POST /api/apps/{app}/processes/{process}/instances/{id}/cancel",
                "POST /api/apps/{app}/processes/{process}/instances/{id}/retry",
                "POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve",
                "POST /api/apps/{app}/entities/{entity}/fields/{field}/files",
                "POST /api/apps/{app}/entities/{entity}/fields/{collection}/{field}/files",
                "GET /api/apps/{app}/files/{id}",
            ])
            .Order(StringComparer.Ordinal);

        Assert.Equal(string.Join('\n', expected), string.Join('\n', marked));
    }

    [Fact]
    public async Task A_service_token_without_a_session_is_a_401_on_every_route_outside_the_service_account_and_endpoint_routes()
    {
        var token = await RequestTokenAsync();
        var others = Routes()
            .Where(route => route.Endpoint.Metadata.GetMetadata<ServiceAccountRoute>() is null)
            .Where(route => !EndpointCaller.IsEndpointPath(new PathString(route.Path)))
            .ToList();
        Assert.NotEmpty(others);

        var answered = new List<string>();
        var accepted = new List<string>();
        foreach (var route in others)
        {
            using var anonymous = await SendAsync(route, bearer: null);
            if (anonymous.StatusCode != HttpStatusCode.Unauthorized)
            {
                answered.Add(route.Name);
                continue;
            }

            using var withToken = await SendAsync(route, token);
            if (withToken.StatusCode != HttpStatusCode.Unauthorized)
            {
                accepted.Add($"{route.Name} answered {(int)withToken.StatusCode}");
            }
        }

        Assert.Equal(string.Join('\n', _anonymousRoutes.Order(StringComparer.Ordinal)), string.Join('\n', answered.Order(StringComparer.Ordinal)));
        Assert.Empty(accepted);
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:Oidc:Authority", _authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting("Tenants:a:Oidc:EndpointAudience", "axis-endpoints");
            builder.UseSetting("Tenants:a:Oidc:ServiceAudience", "axis-services");
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private IEnumerable<ApiRoute> Routes() => ApiRoutes.From(Factory.Services);

    private async Task<string> RequestTokenAsync()
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.ReportingClientId,
            ["client_secret"] = KeycloakFixture.ReportingClientSecret,
        });
        using var response = await client.PostAsync(new Uri($"{_authority}/protocol/openid-connect/token"), form, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var tokens = JsonDocument.Parse(body);
        return tokens.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(ApiRoute route, string? bearer)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), new Uri(route.Path, UriKind.Relative));
        request.Headers.Host = Host;
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (route.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            request.Headers.Add("X-Axis-Csrf", "1");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await Client().SendAsync(request, CancellationToken);
    }

    private HttpClient Client() => _client ?? throw new InvalidOperationException("The test host is not started.");
}
