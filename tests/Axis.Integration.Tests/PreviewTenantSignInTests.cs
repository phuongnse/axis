using System.Net;
using System.Text.Json;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in on a preview tenant through the development realm in Keycloak. Tenant <c>main</c> is served at
/// <c>main.example.test</c> and has the <c>Oidc</c> settings. Its preview tenant, <c>main-preview</c>, is served at
/// <c>localhost</c> and has none, so it signs in with those of <c>main</c>. The realm's client accepts redirects only
/// to <c>http://localhost:5206</c>, so the preview tenant is the one on that host.
/// </summary>
public sealed class PreviewTenantSignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string MainHost = "main.example.test";

    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => _authority ?? throw new InvalidOperationException("The test host is not started.");

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_user_signs_in_on_the_preview_tenants_host_with_only_the_main_tenants_Oidc_settings()
    {
        using var client = CreateClient();

        await SignInAsync(client, "requester");
        var me = await client.GetAsync("/api/me", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("requester", body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Requester User", body.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_session_made_on_the_preview_tenants_host_gets_401_on_the_main_tenants_host()
    {
        using var client = CreateClient();
        var sessionCookie = await SignInAsync(client, "requester");
        using var cookieless = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var onPreview = await GetMeWithCookieAsync(cookieless, "localhost", sessionCookie);
        var onMain = await GetMeWithCookieAsync(cookieless, MainHost, sessionCookie);

        Assert.Equal(HttpStatusCode.OK, onPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, onMain.StatusCode);
        Assert.Equal("application/problem+json", onMain.Content.Headers.ContentType?.MediaType);
    }

    public async ValueTask InitializeAsync()
    {
        var databases = new Dictionary<string, string>();
        foreach (var tenant in new[] { "main", "preview" })
        {
            var connectionString = await database.CreateDatabaseAsync($"previewsignin_{tenant}", CancellationToken);
            databases[tenant] = connectionString;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
            await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        }

        _authority = await keycloak.StartAsync();
        var authority = _authority.ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(databases["main"]);
            builder.UseSetting("Tenants:main:Hosts:0", MainHost);
            builder.UseSetting("Tenants:main:ConnectionString", PostgreSqlServer.RuntimeConnectionString(databases["main"]));
            builder.UseSetting("Tenants:main:SchemaConnectionString", databases["main"]);
            builder.UseSetting("Tenants:main:Oidc:Authority", authority);
            builder.UseSetting("Tenants:main:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:main:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting("Tenants:main:Oidc:EndpointAudience", "axis-endpoints");
            builder.UseSetting("Tenants:main-preview:Hosts:0", "localhost");
            builder.UseSetting("Tenants:main-preview:ConnectionString", PostgreSqlServer.RuntimeConnectionString(databases["preview"]));
            builder.UseSetting("Tenants:main-preview:SchemaConnectionString", databases["preview"]);
            builder.UseSetting("Tenants:main-preview:PreviewOf", "main");
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>Signs in through the server and Keycloak's login form, and returns the session cookie the callback set.</summary>
    private async Task<string> SignInAsync(HttpClient client, string username)
    {
        var challenge = await client.GetAsync("/api/auth/sign-in", CancellationToken);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith($"{Authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);

        // A separate client plays the browser at Keycloak, which marks its cookies Secure. See SignInTests.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var loginPage = await browser.GetAsync(authorize, CancellationToken);
        var html = await loginPage.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(loginPage.StatusCode == HttpStatusCode.OK, html);
        var action = KeycloakSignIn.LoginAction(html);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = KeycloakFixture.Password,
                ["credentialId"] = "",
            }),
        };
        var cookieHeader = string.Join("; ", loginPage.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
        loginRequest.Headers.Add("Cookie", cookieHeader);
        var password = await browser.SendAsync(loginRequest, CancellationToken);
        var login = await KeycloakSignIn.SubmitCodeAsync(browser, password, cookieHeader, username);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal("http://localhost:5206/api/auth/callback", callback.GetLeftPart(UriPartial.Path));

        var signedIn = await client.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        var sessionCookie = signedIn.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie[..cookie.IndexOf(';', StringComparison.Ordinal)])
            .Single(cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        return sessionCookie[(Session.Scheme.Length + 1)..];
    }

    private static async Task<HttpResponseMessage> GetMeWithCookieAsync(HttpClient client, string host, string sessionCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Add("Cookie", $"{Session.Scheme}={sessionCookie}");
        return await client.SendAsync(request, CancellationToken);
    }
}
