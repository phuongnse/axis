using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// Other systems call inbound endpoints with a client credentials token from the tenant's identity
/// provider. Tenant a at localhost and tenant c at c.example.test use the development realm, but only
/// tenant a expects the endpoint audience. Tenant b at b.example.test uses Keycloak's master realm, so
/// it has another issuer. The database is unreachable, so the paths name an application that the
/// name rule refuses: the call finds it without a query, and a valid caller gets a <c>404</c>.
/// </summary>
public sealed class EndpointCallerTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database: the
    // application name in the paths has an underscore, so it is no application without a query.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostA = "localhost";
    private const string HostB = "b.example.test";
    private const string HostC = "c.example.test";
    private const string EndpointPath = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest";
    private const string StatusPath = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest/instances/1";

    private readonly TestClock _clock = new();
    private readonly ConcurrentQueue<EndpointCallerIdentity?> _callers = new();
    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => _authority ?? throw new InvalidOperationException("The test host is not started.");

    private HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_client_credentials_token_without_a_csrf_header_reaches_the_endpoint_as_the_purchasing_system()
    {
        var token = await RequestPurchasingTokenAsync();

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, token);
        using var status = await SendAsync(HttpMethod.Get, StatusPath, HostA, token);

        await AssertProblemAsync(call, HttpStatusCode.NotFound, "No endpoint exists with this name.");
        await AssertProblemAsync(status, HttpStatusCode.NotFound, "No endpoint exists with this name.");
        Assert.Equal(2, _callers.Count);
        Assert.All(_callers, caller =>
        {
            Assert.NotNull(caller);
            Assert.Equal("purchasing-system", caller.Id);
            Assert.Equal("purchasing-system", caller.DisplayName);
            Assert.Equal(["purchasing-system"], caller.Roles);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("wrongly signed")]
    [InlineData("axis client")]
    public async Task An_unusable_token_is_a_401_that_names_no_check(string token)
    {
        var bearer = token switch
        {
            "missing" => null,
            "expired" => await RequestPurchasingTokenAsync(),
            "wrongly signed" => Resign(await RequestPurchasingTokenAsync()),
            "axis client" => await RequestAnnaTokenAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };
        if (token == "expired")
        {
            _clock.Advance(TimeSpan.FromHours(1));
        }

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, bearer);
        using var status = await SendAsync(HttpMethod.Get, StatusPath, HostA, bearer);

        await AssertUnauthorizedAsync(call);
        await AssertUnauthorizedAsync(status);
        Assert.All(_callers, caller => Assert.Null(caller));
    }

    [Fact]
    public async Task A_session_cookie_without_a_token_is_a_401_on_an_endpoint_and_is_not_renewed()
    {
        var sessionCookie = await SignInAnnaAsync();

        using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, bearer: null, sessionCookie);
        using var me = await SendAsync(HttpMethod.Get, "/api/me", HostA, bearer: null, sessionCookie);

        await AssertUnauthorizedAsync(call);
        Assert.False(call.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Theory]
    [InlineData(HostB)]
    [InlineData(HostC)]
    public async Task A_valid_token_of_tenant_a_is_a_401_on_another_tenants_host(string host)
    {
        var token = await RequestPurchasingTokenAsync();

        using var onTenantA = await SendAsync(HttpMethod.Post, EndpointPath, HostA, token);
        using var onOtherTenant = await SendAsync(HttpMethod.Post, EndpointPath, host, token);

        Assert.Equal(HttpStatusCode.NotFound, onTenantA.StatusCode);
        await AssertUnauthorizedAsync(onOtherTenant);
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/sites")]
    public async Task A_valid_token_is_no_session_on_other_api_paths(string path)
    {
        var token = await RequestPurchasingTokenAsync();

        using var response = await SendAsync(HttpMethod.Get, path, HostA, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.All(_callers, caller => Assert.Null(caller));
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        var authority = _authority.ToString();
        var masterRealm = new Uri(_authority, "master").ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            UseTenant(builder, "a", HostA, authority, endpointAudience: "axis-endpoints");
            UseTenant(builder, "b", HostB, masterRealm, endpointAudience: "axis-endpoints");
            UseTenant(builder, "c", HostC, authority, endpointAudience: null);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_clock);
                services.AddSingleton<IStartupFilter>(new CallerStartupFilter(_callers));
            });
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

    private static void UseTenant(IWebHostBuilder builder, string tenant, string host, string authority, string? endpointAudience)
    {
        builder.UseSetting($"Tenants:{tenant}:Hosts:0", host);
        builder.UseSetting($"Tenants:{tenant}:ConnectionString", UnreachableDatabase);
        builder.UseSetting($"Tenants:{tenant}:SchemaConnectionString", UnreachableDatabase);
        builder.UseSetting($"Tenants:{tenant}:Oidc:Authority", authority);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientId", KeycloakFixture.ClientId);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
        if (endpointAudience is not null)
        {
            builder.UseSetting($"Tenants:{tenant}:Oidc:EndpointAudience", endpointAudience);
        }
    }

    private Task<string> RequestPurchasingTokenAsync() =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.PurchasingClientId,
            ["client_secret"] = KeycloakFixture.PurchasingClientSecret,
        });

    // A token the realm issues to the axis client, so its audience is not the endpoint audience.
    private Task<string> RequestAnnaTokenAsync() =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = KeycloakFixture.ClientId,
            ["client_secret"] = KeycloakFixture.ClientSecret,
            ["username"] = "anna",
            ["password"] = KeycloakFixture.Password,
            ["scope"] = "openid",
        });

    private async Task<string> RequestTokenAsync(Dictionary<string, string> fields)
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync(new Uri($"{Authority}/protocol/openid-connect/token"), form, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var tokens = JsonDocument.Parse(body);
        return tokens.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Signs the token's header and payload again with a key the realm never published, keeping its key id.</summary>
    private static string Resign(string token)
    {
        var parts = token.Split('.');
        using var key = RSA.Create(2048);
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(signature)}";
    }

    private async Task<string> SignInAnnaAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/test/session", HostA, bearer: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value[..value.IndexOf(';', StringComparison.Ordinal)])
            .Single(value => value.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        return cookie[(Session.Scheme.Length + 1)..];
    }

    /// <summary>Sends a request without the CSRF header, with the bearer token and session cookie given.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string host, string? bearer, string? sessionCookie = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (sessionCookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={sessionCookie}");
        }

        return await Client.SendAsync(request, CancellationToken);
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response) =>
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "A valid bearer token is needed to call this endpoint.", bearerChallenge: true);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title, bool bearerChallenge = false)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == statusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        if (bearerChallenge)
        {
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        }
        else
        {
            Assert.Empty(response.Headers.WwwAuthenticate);
        }
    }

    /// <summary>
    /// Answers <c>GET /test/session</c> by signing anna in to tenant a, and records the endpoint
    /// caller that authentication found on every other request.
    /// </summary>
    private sealed class CallerStartupFilter(ConcurrentQueue<EndpointCallerIdentity?> callers) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session")
                {
                    await context.SignInAsync(Session.Scheme, Session.CreatePrincipal("a", "anna", "Anna Employee", ["employee"]));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }

                await nextMiddleware(context);
                callers.Enqueue(EndpointCaller.Read(context.User));
            });
            next(app);
        };
    }

    /// <summary>The system clock moved forward by <see cref="Advance"/>, so real tokens are valid until it moves.</summary>
    private sealed class TestClock : TimeProvider
    {
        private TimeSpan _offset;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;

        public void Advance(TimeSpan time) => _offset += time;
    }
}
