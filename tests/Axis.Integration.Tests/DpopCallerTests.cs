using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Server.Authentication;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Axis.Integration.Tests;

/// <summary>
/// Under the <c>fapi2</c> sign-in profile, the endpoint and record routes accept only an access token that is bound to a
/// DPoP key, with a proof made by that key for the request. Tenant a is <c>fapi2</c> at localhost and at
/// fapi.example.test, and tenant b is <c>standard</c> at b.example.test. Both use the development realm. The database is
/// unreachable, so the paths name an application that the name rule refuses: a valid caller gets the endpoint's own
/// <c>404</c>.
/// </summary>
public sealed class DpopCallerTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostA = "localhost";
    private const string ForwardedHost = "fapi.example.test";
    private const string HostB = "b.example.test";
    private const string EndpointPath = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest";
    private const string StatusPath = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest/instances/1";
    private const string NotFoundTitle = "No endpoint exists with this name.";
    private const string DpopChallenge = "DPoP algs=\"ES256 PS256\"";

    private readonly ConcurrentQueue<EndpointCallerIdentity?> _callers = new();
    private readonly DpopTestKey _key = new();
    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => _authority ?? throw new InvalidOperationException("The test host is not started.");

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    private HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task The_realm_issues_a_token_that_is_bound_to_the_key_of_the_proof()
    {
        var token = await RequestTokenAsync(PurchasingFields(), _key, dpop: true);

        var parsed = new JsonWebToken(token);
        Assert.True(parsed.TryGetPayloadValue<JsonElement>("cnf", out var cnf));
        Assert.Equal(_key.Thumbprint, cnf.GetProperty("jkt").GetString());
    }

    [Fact]
    public async Task A_bound_token_with_a_valid_proof_reaches_the_endpoint_as_the_purchasing_system()
    {
        var token = await RequestTokenAsync(PurchasingFields(), _key, dpop: true);

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, "DPoP", token, Proof("POST", EndpointPath, HostA, token));
        using var status = await SendAsync(HttpMethod.Get, StatusPath, HostA, "DPoP", token, Proof("GET", StatusPath, HostA, token));

        await AssertProblemAsync(call, HttpStatusCode.NotFound, NotFoundTitle);
        await AssertProblemAsync(status, HttpStatusCode.NotFound, NotFoundTitle);
        Assert.Equal(2, _callers.Count);
        Assert.All(_callers, caller =>
        {
            Assert.NotNull(caller);
            Assert.Equal("purchasing-system", caller.Id);
            Assert.Equal(["purchasing-system"], caller.Roles);
        });
    }

    [Theory]
    [InlineData("a plain token under Bearer")]
    [InlineData("a plain token under DPoP with a proof")]
    [InlineData("a bound token under Bearer with a proof")]
    [InlineData("a bound token without a proof")]
    [InlineData("a proof for another host")]
    [InlineData("a proof of another key")]
    [InlineData("a proof for another method")]
    [InlineData("a proof for another token")]
    public async Task A_call_that_is_not_bound_to_the_key_is_a_401_with_a_DPoP_challenge(string kind)
    {
        using var other = new DpopTestKey();
        var bound = await RequestTokenAsync(PurchasingFields(), _key, dpop: true);
        var plain = await RequestTokenAsync(PurchasingFields(), key: null, dpop: false);
        var (scheme, token, proof) = kind switch
        {
            "a plain token under Bearer" => ("Bearer", plain, null),
            "a plain token under DPoP with a proof" => ("DPoP", plain, Proof("POST", EndpointPath, HostA, plain)),
            "a bound token under Bearer with a proof" => ("Bearer", bound, Proof("POST", EndpointPath, HostA, bound)),
            "a bound token without a proof" => ("DPoP", bound, null),
            "a proof for another host" => ("DPoP", bound, Proof("POST", EndpointPath, HostB, bound)),
            "a proof of another key" => ("DPoP", bound, other.Proof("POST", Url("POST", EndpointPath, HostA), bound)),
            "a proof for another method" => ("DPoP", bound, Proof("GET", EndpointPath, HostA, bound)),
            "a proof for another token" => ("DPoP", bound, Proof("POST", EndpointPath, HostA, plain)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, scheme, token, proof);
        using var status = await SendAsync(HttpMethod.Get, StatusPath, HostA, scheme, token, proof);

        await AssertUnauthorizedAsync(call);
        await AssertUnauthorizedAsync(status);
        Assert.All(_callers, caller => Assert.Null(caller));
    }

    [Fact]
    public async Task A_stale_proof_is_a_401()
    {
        var token = await RequestTokenAsync(PurchasingFields(), _key, dpop: true);
        var proof = _key.Proof("POST", Url("POST", EndpointPath, HostA), token, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, "DPoP", token, proof);

        await AssertUnauthorizedAsync(call);
    }

    [Fact]
    public async Task A_proof_that_names_the_forwarded_scheme_and_host_passes_through_the_listed_proxy()
    {
        var token = await RequestTokenAsync(PurchasingFields(), _key, dpop: true);
        var forwarded = _key.Proof("POST", $"https://{ForwardedHost}{EndpointPath}", token);
        var internalAddress = _key.Proof("POST", $"http://server.internal{EndpointPath}", token);
        var unforwarded = _key.Proof("POST", $"http://{ForwardedHost}{EndpointPath}", token);

        var passed = await SendThroughProxyAsync(token, forwarded);
        var refusedInternal = await SendThroughProxyAsync(token, internalAddress);
        var refusedUnforwarded = await SendThroughProxyAsync(token, unforwarded);

        Assert.Equal(StatusCodes.Status404NotFound, passed.Response.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, refusedInternal.Response.StatusCode);
        Assert.Equal(DpopChallenge, refusedInternal.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal(StatusCodes.Status401Unauthorized, refusedUnforwarded.Response.StatusCode);
    }

    [Fact]
    public async Task A_standard_tenant_still_accepts_a_plain_bearer_token()
    {
        var plain = await RequestTokenAsync(PurchasingFields(), key: null, dpop: false);

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostB, "Bearer", plain, proof: null);

        await AssertProblemAsync(call, HttpStatusCode.NotFound, NotFoundTitle, challenge: null);
        Assert.Equal("purchasing-system", Assert.Single(_callers)?.Id);
    }

    [Fact]
    public async Task Every_route_that_accepts_a_bearer_token_refuses_a_plain_one_on_a_fapi2_tenant()
    {
        var tokens = new[]
        {
            await RequestTokenAsync(PurchasingFields(), key: null, dpop: false),
            await RequestTokenAsync(ReportingFields(), key: null, dpop: false),
        };
        var selector = Factory.Services.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get(EndpointCaller.RequestScheme).ForwardDefaultSelector
            ?? throw new InvalidOperationException("The request scheme has no selector.");
        var bearerRoutes = ApiRoutes.From(Factory.Services)
            .Where(route => selector(RouteContext(route)) != Session.Scheme)
            .ToList();

        Assert.Contains(bearerRoutes, route => EndpointCaller.IsEndpointPath(new PathString(route.Path)));
        Assert.Contains(bearerRoutes, route => route.Endpoint.Metadata.GetMetadata<ServiceAccountRoute>() is not null);
        foreach (var operatorRoute in new[]
        {
            "POST /api/apps/{app}/processes/{process}/instances/{id}/cancel",
            "POST /api/apps/{app}/processes/{process}/instances/{id}/retry",
            "POST /api/apps/{app}/processes/{process}/instances/{id}/calls/{callId}/resolve",
        })
        {
            Assert.Contains(bearerRoutes, route => route.Name == operatorRoute);
        }

        var accepted = new List<string>();
        foreach (var route in bearerRoutes)
        {
            foreach (var token in tokens)
            {
                using var response = await SendAsync(route, token);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    accepted.Add($"{route.Name} answered {(int)response.StatusCode}");
                }
            }
        }

        Assert.Empty(accepted);
        Assert.All(_callers, caller => Assert.Null(caller));
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        var authority = _authority.ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", "10.0.0.5");
            UseTenant(builder, "a", [HostA, ForwardedHost], authority, fapi2: true);
            UseTenant(builder, "b", [HostB], authority, fapi2: false);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new CallerStartupFilter(_callers)));
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    public async ValueTask DisposeAsync()
    {
        _key.Dispose();
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static void UseTenant(IWebHostBuilder builder, string tenant, string[] hosts, string authority, bool fapi2)
    {
        for (var index = 0; index < hosts.Length; index++)
        {
            builder.UseSetting($"Tenants:{tenant}:Hosts:{index}", hosts[index]);
        }

        builder.UseSetting($"Tenants:{tenant}:ConnectionString", UnreachableDatabase);
        builder.UseSetting($"Tenants:{tenant}:SchemaConnectionString", UnreachableDatabase);
        builder.UseSetting($"Tenants:{tenant}:Oidc:Authority", authority);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientId", KeycloakFixture.ClientId);
        builder.UseSetting($"Tenants:{tenant}:Oidc:EndpointAudience", "axis-endpoints");
        builder.UseSetting($"Tenants:{tenant}:Oidc:ServiceAudience", "axis-services");
        if (fapi2)
        {
            using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            builder.UseSetting($"Tenants:{tenant}:Oidc:Profile", "fapi2");
            builder.UseSetting($"Tenants:{tenant}:Oidc:ClientKey", clientKey.ExportPkcs8PrivateKeyPem());
            builder.UseSetting($"Tenants:{tenant}:Oidc:ClientKeyId", "key-1");
        }
        else
        {
            builder.UseSetting($"Tenants:{tenant}:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
        }
    }

    private static Dictionary<string, string> PurchasingFields() => new()
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = KeycloakFixture.PurchasingClientId,
        ["client_secret"] = KeycloakFixture.PurchasingClientSecret,
    };

    private static Dictionary<string, string> ReportingFields() => new()
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = KeycloakFixture.ReportingClientId,
        ["client_secret"] = KeycloakFixture.ReportingClientSecret,
    };

    /// <summary>Gets a client credentials token. With <paramref name="dpop"/> the request carries a proof, so the realm binds the token to the key.</summary>
    private async Task<string> RequestTokenAsync(Dictionary<string, string> fields, DpopTestKey? key, bool dpop)
    {
        var url = $"{Authority}/protocol/openid-connect/token".Replace("//protocol", "/protocol", StringComparison.Ordinal);
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(fields);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url)) { Content = form };
        if (dpop)
        {
            request.Headers.Add("DPoP", (key ?? throw new ArgumentNullException(nameof(key))).Proof("POST", url, "", includeAth: false));
        }

        using var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var tokens = JsonDocument.Parse(body);
        Assert.Equal(dpop ? "DPoP" : "Bearer", tokens.RootElement.GetProperty("token_type").GetString(), ignoreCase: true);
        return tokens.RootElement.GetProperty("access_token").GetString()!;
    }

    // The test client's requests are plain http, so the address a proof names is http and the host the request is sent to.
    private static string Url(string method, string path, string host) => $"http://{host}{path}";

    private string Proof(string method, string path, string host, string token) => _key.Proof(method, Url(method, path, host), token);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string host, string scheme, string token, string? proof)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        if (proof is not null)
        {
            request.Headers.Add("DPoP", proof);
        }

        return await Client.SendAsync(request, CancellationToken);
    }

    /// <summary>Sends a plain bearer token to a route of the <c>fapi2</c> tenant, with the CSRF header and a body for a write.</summary>
    private async Task<HttpResponseMessage> SendAsync(ApiRoute route, string token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), new Uri(route.Path, UriKind.Relative));
        request.Headers.Host = HostA;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (route.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            request.Headers.Add("X-Axis-Csrf", "1");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await Client.SendAsync(request, CancellationToken);
    }

    /// <summary>
    /// Sends the call through the listed proxy at 10.0.0.5, which forwards https and the host of tenant a. The server
    /// itself is reached at server.internal.
    /// </summary>
    private async Task<HttpContext> SendThroughProxyAsync(string token, string proof) =>
        await Factory.Server.SendAsync(
            context =>
            {
                context.Request.Method = HttpMethods.Post;
                context.Request.Path = EndpointPath;
                context.Request.Host = new HostString("server.internal");
                context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
                context.Request.Headers["X-Forwarded-Proto"] = "https";
                context.Request.Headers["X-Forwarded-Host"] = ForwardedHost;
                context.Request.Headers.Authorization = $"DPoP {token}";
                context.Request.Headers["DPoP"] = proof;
            },
            CancellationToken);

    private static DefaultHttpContext RouteContext(ApiRoute route)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = route.Method;
        context.Request.Path = route.Path;
        context.Request.Headers.Authorization = "Bearer token";
        context.SetEndpoint(route.Endpoint);
        return context;
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{response.StatusCode}: {body}");
        Assert.Equal(DpopChallenge, response.Headers.WwwAuthenticate.ToString());
        Assert.DoesNotContain("DPoP", body, StringComparison.Ordinal);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title, string? challenge = null)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == statusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(challenge ?? "", response.Headers.WwwAuthenticate.ToString());
    }

    /// <summary>Records the endpoint caller that authentication found on every request.</summary>
    private sealed class CallerStartupFilter(ConcurrentQueue<EndpointCallerIdentity?> callers) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                await nextMiddleware(context);
                callers.Enqueue(EndpointCaller.Read(context.User));
            });
            next(app);
        };
    }
}
