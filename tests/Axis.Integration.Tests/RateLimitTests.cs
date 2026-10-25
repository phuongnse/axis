using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// A caller past a rate limit gets a <c>429</c> with <c>Retry-After</c>, and a caller in another partition, another
/// user, another client address or another tenant still passes. The limits are low, and the allowance never refills
/// during a test. Tenant a at localhost takes endpoint callers, and tenant c at c.example.test is a second tenant.
/// The database is unreachable, so the paths need no query: <c>/api/me</c> reads only the session, and the endpoint
/// path names an application that the name rule refuses, so a valid caller gets a <c>404</c>.
/// </summary>
public sealed class RateLimitTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostA = "localhost";
    private const string HostC = "c.example.test";
    private const string EndpointPath = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest";
    private const string StatusPath = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest/instances/1";
    private const string ValidatePath = "/api/studio/apps/x/validate";
    private const string AddressHeader = "X-Test-Client-Address";

    private const int SignInLimit = 3;
    private const int SessionLimit = 5;
    private const int BearerLimit = 4;
    private const int PublishingLimit = 2;

    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => _authority ?? throw new InvalidOperationException("The test host is not started.");

    private HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_user_past_the_session_limit_is_refused_while_another_user_tenant_or_partition_still_passes()
    {
        var anna = await SignInAsync("a", "anna", HostA);
        var annaOnC = await SignInAsync("c", "anna", HostC);
        var ben = await SignInAsync("a", "ben", HostA);

        for (var count = 0; count < SessionLimit; count++)
        {
            using var me = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: anna);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using var refused = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: anna);
        using var otherAddress = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: anna, address: "10.0.0.9");
        using var otherUser = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: ben);
        using var otherTenant = await SendAsync(HttpMethod.Get, "/api/me", HostC, session: annaOnC);
        using var publishing = await SendAsync(HttpMethod.Post, ValidatePath, HostA, session: anna, csrf: true);
        using var open = await SendAsync(HttpMethod.Get, "/health/live", HostA);

        await AssertRefusedAsync(refused);
        await AssertRefusedAsync(otherAddress);
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherTenant.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, publishing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    [Fact]
    public async Task A_user_past_the_publishing_limit_is_refused_while_the_session_and_another_user_still_pass()
    {
        var anna = await SignInAsync("a", "anna", HostA);
        var ben = await SignInAsync("a", "ben", HostA);

        for (var count = 0; count < PublishingLimit; count++)
        {
            using var validate = await SendAsync(HttpMethod.Post, ValidatePath, HostA, session: anna, csrf: true);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, validate.StatusCode);
        }

        using var refused = await SendAsync(HttpMethod.Post, ValidatePath, HostA, session: anna, csrf: true);
        using var publish = await SendAsync(HttpMethod.Post, "/api/studio/apps/x/publish", HostA, session: anna, csrf: true);
        using var status = await SendAsync(HttpMethod.Get, "/api/studio/apps/x/requests/1", HostA, session: anna);
        using var me = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: anna);
        using var otherUser = await SendAsync(HttpMethod.Post, ValidatePath, HostA, session: ben, csrf: true);

        await AssertRefusedAsync(refused);
        await AssertRefusedAsync(publish);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherUser.StatusCode);
    }

    [Fact]
    public async Task A_client_address_past_the_sign_in_limit_is_refused_and_the_callback_shares_the_count()
    {
        for (var count = 0; count < SignInLimit; count++)
        {
            using var signIn = await SendAsync(HttpMethod.Get, "/api/auth/sign-in", HostA, address: "10.0.0.1");
            Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        }

        using var refused = await SendAsync(HttpMethod.Get, "/api/auth/sign-in", HostA, address: "10.0.0.1");
        using var callback = await SendAsync(HttpMethod.Get, "/api/auth/callback", HostA, address: "10.0.0.1");
        using var otherAddress = await SendAsync(HttpMethod.Get, "/api/auth/sign-in", HostA, address: "10.0.0.2");
        using var otherTenant = await SendAsync(HttpMethod.Get, "/api/auth/sign-in", HostC, address: "10.0.0.1");

        await AssertRefusedAsync(refused);
        await AssertRefusedAsync(callback);
        await AssertRefusedAsync(otherTenant);
        Assert.Equal(HttpStatusCode.Redirect, otherAddress.StatusCode);

        // The callback of another address takes one of the same three.
        for (var count = 0; count < SignInLimit - 1; count++)
        {
            using var signIn = await SendAsync(HttpMethod.Get, "/api/auth/sign-in", HostA, address: "10.0.0.3");
            Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        }

        using var counted = await SendAsync(HttpMethod.Get, "/api/auth/callback", HostA, address: "10.0.0.3");
        using var afterCallback = await SendAsync(HttpMethod.Get, "/api/auth/sign-in", HostA, address: "10.0.0.3");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, counted.StatusCode);
        await AssertRefusedAsync(afterCallback);
    }

    [Fact]
    public async Task A_valid_bearer_caller_past_the_bearer_limit_is_refused_while_an_invalid_token_still_gets_a_401()
    {
        var token = await RequestPurchasingTokenAsync();
        var anna = await SignInAsync("a", "anna", HostA);

        for (var count = 0; count < BearerLimit; count++)
        {
            using var call = await SendAsync(HttpMethod.Post, EndpointPath, HostA, bearer: token);
            Assert.Equal(HttpStatusCode.NotFound, call.StatusCode);
        }

        using var refused = await SendAsync(HttpMethod.Post, EndpointPath, HostA, bearer: token);
        using var status = await SendAsync(HttpMethod.Get, StatusPath, HostA, bearer: token);
        using var resigned = await SendAsync(HttpMethod.Post, EndpointPath, HostA, bearer: Resign(token));
        using var missing = await SendAsync(HttpMethod.Post, EndpointPath, HostA);
        using var me = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: anna);

        await AssertRefusedAsync(refused);
        await AssertRefusedAsync(status);
        Assert.Equal(HttpStatusCode.Unauthorized, resigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_refused_write_gets_its_csrf_403_and_an_anonymous_request_its_401_before_any_429()
    {
        var anna = await SignInAsync("a", "anna", HostA);
        for (var count = 0; count < SessionLimit; count++)
        {
            using var me = await SendAsync(HttpMethod.Get, "/api/me", HostA, session: anna);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using var withoutHeader = await SendAsync(HttpMethod.Post, "/api/auth/sign-out", HostA, session: anna);
        using var withHeader = await SendAsync(HttpMethod.Post, "/api/auth/sign-out", HostA, session: anna, csrf: true);
        using var anonymous = await SendAsync(HttpMethod.Get, "/api/me", HostA);

        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);
        using (var problem = JsonDocument.Parse(await withoutHeader.Content.ReadAsStringAsync(CancellationToken)))
        {
            Assert.Equal("The request must carry the X-Axis-Csrf header.", problem.RootElement.GetProperty("title").GetString());
        }

        await AssertRefusedAsync(withHeader);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
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
            UseTenant(builder, "a", HostA, authority, endpointAudience: "axis-endpoints");
            UseTenant(builder, "c", HostC, authority, endpointAudience: null);
            builder.ConfigureTestServices(services =>
            {
                // The allowance never refills during a test.
                services.AddSingleton(new RateLimitSettings(SignInLimit, SessionLimit, BearerLimit, PublishingLimit)
                {
                    ReplenishmentPeriod = TimeSpan.FromHours(1),
                });
                services.AddSingleton<IStartupFilter, TestRequestStartupFilter>();
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

    private async Task<string> RequestPurchasingTokenAsync()
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.PurchasingClientId,
            ["client_secret"] = KeycloakFixture.PurchasingClientSecret,
        });
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

    /// <summary>Signs <paramref name="user"/> in to <paramref name="tenant"/> and returns the session cookie value.</summary>
    private async Task<string> SignInAsync(string tenant, string user, string host)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/test/session?tenant={tenant}&user={user}", host);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value[..value.IndexOf(';', StringComparison.Ordinal)])
            .Single(value => value.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        return cookie[(Session.Scheme.Length + 1)..];
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string host,
        string? session = null,
        string? bearer = null,
        string address = "10.1.1.1",
        bool csrf = false)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Add(AddressHeader, address);
        if (csrf)
        {
            request.Headers.Add(CsrfHeader.Name, "1");
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (session is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={session}");
        }

        return await Client.SendAsync(request, CancellationToken);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.TooManyRequests, $"{response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("Too many requests.", problem.RootElement.GetProperty("title").GetString());
        Assert.True(int.TryParse(response.Headers.GetValues("Retry-After").Single(), out var seconds) && seconds > 0);
    }

    /// <summary>
    /// Takes the client address from <see cref="AddressHeader"/>, and answers <c>GET /test/session?tenant=&amp;user=</c> by
    /// signing the user in to the tenant. Both come before every other middleware.
    /// </summary>
    private sealed class TestRequestStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers[AddressHeader].ToString() is { Length: > 0 } address)
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                }

                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session")
                {
                    var tenant = context.Request.Query["tenant"].ToString();
                    var user = context.Request.Query["user"].ToString();
                    await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenant, user, user, ["employee"]));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
