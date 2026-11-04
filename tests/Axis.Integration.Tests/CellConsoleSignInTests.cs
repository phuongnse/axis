using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Server.Authentication;
using Axis.Server.CellApi;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// The sign-in of an operator on the cell console host, with real Keycloak and PostgreSQL. The operator realm is a realm of its
/// own, made for the test, with the role <c>cell-operator</c>, the confidential client <c>axis-cell-console</c> and two people:
/// <c>operator</c> has the role and <c>stranger</c> has not. Each has a password and an authenticator. The platform has the
/// tenants of <see cref="CellTestPlatform"/>, and tenant a is served at <c>localhost</c>. A test clock, which starts at the
/// real time, replaces the server's clock, and the tests move it only after sign-in.
/// </summary>
public sealed class CellConsoleSignInTests(PostgreSqlFixture database, KeycloakFixture keycloak)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string OperatorRole = "cell-operator";
    private const string ConsoleClientId = "axis-cell-console";
    private const string ConsoleClientSecret = "console-development-secret";
    private const string TotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string Sessions = "SELECT count(*) FROM platform.operator_sessions";

    // The development realm's client accepts redirects to http://localhost:5206/*.
    private static readonly Uri _tenantHost = new("http://localhost:5206");
    private static readonly Uri _console = new("http://console.test");

    private readonly HttpClient _http = new();
    private readonly TestClock _clock = new();
    private readonly string _realm = $"ops-{Guid.NewGuid():N}"[..16];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "admin/realms"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        request.Content = JsonContent.Create(new
        {
            realm = _realm,
            enabled = true,
            loginTheme = "axis",
            roles = new { realm = new[] { new { name = OperatorRole } } },
            clients = new[]
            {
                new
                {
                    clientId = ConsoleClientId,
                    enabled = true,
                    publicClient = false,
                    secret = ConsoleClientSecret,
                    standardFlowEnabled = true,
                    directAccessGrantsEnabled = false,
                    redirectUris = new[] { new Uri(_console, CellConsoleSignIn.CallbackPath).AbsoluteUri },
                },
            },
            users = new[] { User("operator", [OperatorRole]), User("stranger", []) },
        });
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{_realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    [Fact]
    public async Task An_operator_with_the_role_signs_in_on_the_console_host_and_reads_the_tenants_with_the_session_cookie_alone()
    {
        var (factory, platform) = await StartAsync();
        await using var _ = factory;
        using var client = CreateClient(factory, _console);

        await SignInAsync(client, "operator");
        using var list = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(["a", "b", "c"], items.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.Equal(1L, await CountSessionsAsync(platform));
    }

    [Fact]
    public async Task A_signed_in_operator_without_the_role_gets_403()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var client = CreateClient(factory, _console);

        await SignInAsync(client, "stranger");
        using var list = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task A_tenant_session_cookie_gets_401_on_the_console_host_under_both_cookie_names()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var tenantClient = CreateClient(factory, _tenantHost);
        using var consoleClient = CreateClient(factory, _console, handleCookies: false);

        var callback = await KeycloakSignIn.LogInAsync(tenantClient, await keycloak.StartAsync(), "requester", _tenantHost);
        using var signedIn = await tenantClient.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var tenantSession = signedIn.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Split(';')[0];
        using var onTenant = await tenantClient.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, onTenant.StatusCode);

        using var asTenantCookie = await GetWithCookieAsync(consoleClient, $"{Session.Scheme}={tenantSession}");
        using var asConsoleCookie = await GetWithCookieAsync(consoleClient, $"{CellOperatorSession.PlainCookieName}={tenantSession}");

        Assert.Equal(HttpStatusCode.Unauthorized, asTenantCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asConsoleCookie.StatusCode);
    }

    [Fact]
    public async Task Sign_out_needs_the_CSRF_header_and_then_ends_the_session()
    {
        var (factory, platform) = await StartAsync();
        await using var _ = factory;
        using var client = CreateClient(factory, _console);
        await SignInAsync(client, "operator");

        using var withoutHeader = await client.PostAsync(new Uri("/api/cell/auth/sign-out", UriKind.Relative), content: null, CancellationToken);
        using var stillReads = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillReads.StatusCode);
        Assert.Equal(1L, await CountSessionsAsync(platform));

        using var signOut = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/cell/auth/sign-out", UriKind.Relative));
        signOut.Headers.Add("X-Axis-Csrf", "1");
        using var withHeader = await client.SendAsync(signOut, CancellationToken);
        using var afterSignOut = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, withHeader.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
        Assert.Equal(0L, await CountSessionsAsync(platform));
    }

    [Fact]
    public async Task The_session_ends_after_fifteen_idle_minutes_and_its_row_is_deleted()
    {
        var (factory, platform) = await StartAsync();
        await using var _ = factory;
        using var client = CreateClient(factory, _console);
        await SignInAsync(client, "operator");

        _clock.Advance(TimeSpan.FromMinutes(14));
        using var beforeIdleLimit = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(16));
        using var afterIdleLimit = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, beforeIdleLimit.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterIdleLimit.StatusCode);
        Assert.Equal(0L, await CountSessionsAsync(platform));
    }

    [Fact]
    public async Task The_session_ends_eight_hours_after_sign_in_even_with_steady_use_and_its_row_is_deleted()
    {
        var (factory, platform) = await StartAsync();
        await using var _ = factory;
        using var client = CreateClient(factory, _console);
        await SignInAsync(client, "operator");

        // Every 10 minutes up to 7 hours 50 minutes after sign-in.
        for (var request = 1; request <= 47; request++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            using var steady = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, steady.StatusCode);
        }

        Assert.Equal(1L, await CountSessionsAsync(platform));
        _clock.Advance(TimeSpan.FromMinutes(11));
        using var afterLimit = await client.GetAsync(new Uri("/api/cell/tenants", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, afterLimit.StatusCode);
        Assert.Equal(0L, await CountSessionsAsync(platform));
    }

    [Fact]
    public async Task A_bearer_token_wins_over_the_session_cookie()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var client = CreateClient(factory, _console);
        await SignInAsync(client, "operator");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/cell/tenants", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_the_client_secret_the_console_offers_no_sign_in()
    {
        var (factory, _) = await StartAsync(withSecret: false);
        await using var _ = factory;
        using var client = CreateClient(factory, _console);

        using var response = await client.GetAsync(new Uri("/api/cell/auth/sign-in", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static object User(string username, string[] roles) => new
    {
        username,
        email = $"{username}@example.test",
        emailVerified = true,
        firstName = username,
        lastName = "Operator",
        enabled = true,
        realmRoles = roles,
        credentials = new object[]
        {
            new { type = "password", value = KeycloakFixture.Password, temporary = false },
            new
            {
                type = "otp",
                secretData = $$"""{"value":"{{TotpSecret}}"}""",
                credentialData = """{"subType":"totp","digits":6,"counter":0,"period":30,"algorithm":"HmacSHA1","secretEncoding":"BASE32"}""",
            },
        },
    };

    // Logs in at the operator realm and completes the callback on the console client, which keeps the session cookie.
    private async Task SignInAsync(HttpClient client, string username)
    {
        var callback = await KeycloakSignIn.LogInAsync(
            client,
            new Uri(_baseAddress, $"realms/{_realm}"),
            username,
            _console,
            signInPath: "/api/cell/auth/sign-in",
            callbackPath: CellConsoleSignIn.CallbackPath,
            code: DevelopmentTotp.CodeFromSecret(TotpSecret));
        using var signedIn = await client.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal("/", signedIn.Headers.Location!.OriginalString);
    }

    private static async Task<HttpResponseMessage> GetWithCookieAsync(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/cell/tenants", UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, CancellationToken);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, Uri address, bool handleCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = address,
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

    private static async Task<long> CountSessionsAsync(string platform) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(platform, Sessions, CancellationToken))!;

    private async Task<string> TokenAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await _http.PostAsync(
            new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<(WebApplicationFactory<Program> Factory, string Platform)> StartAsync(bool withSecret = true)
    {
        var authority = await keycloak.StartAsync();
        var platform = await CellTestPlatform.CreateAsync(database, authority, CancellationToken);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:ConsoleHost", _console.Host);
            builder.UseSetting("Cell:OperatorRealm", _realm);
            builder.UseSetting("Cell:OperatorRole", OperatorRole);
            builder.UseSetting("IdentityProvider:Url", _baseAddress.ToString());
            if (withSecret)
            {
                builder.UseSetting(CellConsoleSignIn.ClientSecretSetting, ConsoleClientSecret);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(_clock));
        });
        return (factory, platform);
    }

    /// <summary>The real time plus the time that a test has moved it, so a sign-in at Keycloak sees a believable <c>auth_time</c>.</summary>
    private sealed class TestClock : TimeProvider
    {
        private TimeSpan _offset;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;

        public void Advance(TimeSpan time) => _offset += time;
    }
}
