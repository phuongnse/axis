using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in through Keycloak for two directory tenants. Tenant a is served at <c>localhost</c> and tenant b at
/// <c>127.0.0.1</c>, and each signs in with the OIDC client of its own directory entry, whose secret is stored
/// encrypted. The development realm accepts the callback of each host. Each tenant has a migrated database, where
/// a completed sign-in writes its audit record.
/// </summary>
public sealed partial class DirectorySignInTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string TestSecretKey = TestPlatform.SecretKey;

    // The realm's client accepts redirects to these two addresses, whatever the port.
    private static readonly Uri _addressA = new("http://localhost:5206");
    private static readonly Uri _addressB = new("http://127.0.0.1:5280");

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private readonly List<string> _serverResponses = [];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_signs_in_on_each_directory_tenants_host_and_the_session_is_that_tenants_only()
    {
        var (factory, logs, platform, authority) = await StartAsync();
        await using var _ = factory;
        using var clientA = CreateClient(factory, _addressA);
        using var clientB = CreateClient(factory, _addressB);
        using var cookieless = CreateClient(factory, _addressB, handleCookies: false);

        var signInA = await SignInAsync(clientA, authority, "anna", _addressA);
        var signInB = await SignInAsync(clientB, authority, "binh", _addressB);

        Assert.Equal("anna", await MeAsync(clientA));
        Assert.Equal("binh", await MeAsync(clientB));
        using var crossed = await GetMeWithCookieAsync(cookieless, signInA.SessionCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, crossed.StatusCode);
        Assert.NotEmpty(signInB.SessionCookie);
        await AssertNoClientSecretAsync(logs, platform);
    }

    [Fact]
    public async Task A_callback_that_arrives_on_the_other_tenants_host_is_ignored_and_completes_on_its_own_host()
    {
        var (factory, logs, platform, authority) = await StartAsync();
        await using var _ = factory;
        using var clientA = CreateClient(factory, _addressA);
        using var clientB = CreateClient(factory, _addressB);
        var callback = await LogInAtKeycloakAsync(clientA, authority, "anna", _addressA);

        using var onB = await SendAsync(clientB, callback.PathAndQuery);
        using var meOnB = await SendAsync(clientB, "/api/me");

        Assert.DoesNotContain(
            onB.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, meOnB.StatusCode);

        using var onA = await SendAsync(clientA, callback.PathAndQuery);

        Assert.Equal(HttpStatusCode.Found, onA.StatusCode);
        Assert.Contains(onA.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        Assert.Equal("anna", await MeAsync(clientA));
        await AssertNoClientSecretAsync(logs, platform);
    }

    private async Task<(WebApplicationFactory<Program> Factory, LogCollector Logs, string Platform, Uri Authority)> StartAsync()
    {
        var authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("signin_platform", CancellationToken);
        var tenantDatabases = new Dictionary<string, NpgsqlConnectionStringBuilder>();
        foreach (var id in new[] { "a", "b" })
        {
            var connectionString = await database.CreateDatabaseAsync($"signin_{id}", CancellationToken);
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(CancellationToken);
                await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
                await using var data = DataDatabaseFixture.CreateContext(connection);
                await configuration.Database.MigrateAsync(CancellationToken);
                await data.Database.MigrateAsync(CancellationToken);
            }

            await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
            tenantDatabases[id] = new NpgsqlConnectionStringBuilder(connectionString);
        }

        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
            foreach (var (id, host) in new[] { ("a", "localhost"), ("b", "127.0.0.1") })
            {
                context.Tenants.Add(new TenantRow
                {
                    Id = id,
                    Status = "active",
                    SchemaVersion = TenantSchemaVersion.Current,
                    DatabaseHost = tenantDatabases[id].Host!,
                    DatabasePort = tenantDatabases[id].Port,
                    DatabaseName = tenantDatabases[id].Database!,
                    RuntimeUser = PostgreSqlServer.RuntimeRole,
                    SchemaUser = PostgreSqlServer.SchemaRole,
                    RuntimePassword = _cipher.Encrypt(id, TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
                    SchemaPassword = _cipher.Encrypt(id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                    OidcAuthority = authority.ToString(),
                    OidcClientId = KeycloakFixture.ClientId,
                    OidcClientSecret = _cipher.Encrypt(id, TenantSecretFields.OidcClientSecret, KeycloakFixture.ClientSecret),
                });
                context.TenantHosts.Add(new TenantHostRow { TenantId = id, Host = host });
            }

            await context.SaveChangesAsync(CancellationToken);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        var logs = new LogCollector();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(platform);
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        });
        return (factory, logs, platform, authority);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, Uri address, bool handleCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = address,
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

    /// <summary>
    /// Starts sign-in on the server and logs in at Keycloak's login form as a browser would. Returns the callback
    /// address that Keycloak sends the browser to, which has not been called.
    /// </summary>
    private async Task<Uri> LogInAtKeycloakAsync(HttpClient client, Uri authority, string username, Uri expectedBase)
    {
        using var challenge = await SendAsync(client, "/api/auth/sign-in");
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith($"{authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString($"{expectedBase.GetLeftPart(UriPartial.Authority)}/api/auth/callback"), authorize.Query, StringComparison.Ordinal);

        // A separate client plays the browser at Keycloak. Keycloak marks its cookies Secure, which a
        // browser sends to http://localhost but a CookieContainer does not, so the client carries them itself.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var loginPage = await browser.GetAsync(authorize, CancellationToken);
        var html = await loginPage.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(loginPage.StatusCode == HttpStatusCode.OK, html);
        var action = WebUtility.HtmlDecode(FormActionPattern().Match(html).Groups["action"].Value);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(action))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = KeycloakFixture.Password,
                ["credentialId"] = "",
            }),
        };
        loginRequest.Headers.Add("Cookie", string.Join("; ", loginPage.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';')[0])));
        var login = await browser.SendAsync(loginRequest, CancellationToken);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal($"{expectedBase.GetLeftPart(UriPartial.Authority)}/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        return callback;
    }

    private async Task<SignInResult> SignInAsync(HttpClient client, Uri authority, string username, Uri expectedBase)
    {
        var callback = await LogInAtKeycloakAsync(client, authority, username, expectedBase);
        using var signedIn = await SendAsync(client, callback.PathAndQuery);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        var sessionCookie = signedIn.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie[..cookie.IndexOf(';', StringComparison.Ordinal)])
            .Single(cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        return new SignInResult(sessionCookie[(Session.Scheme.Length + 1)..]);
    }

    private async Task<string?> MeAsync(HttpClient client)
    {
        using var me = await SendAsync(client, "/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetString();
    }

    private async Task<HttpResponseMessage> GetMeWithCookieAsync(HttpClient client, string sessionCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Add("Cookie", $"{Session.Scheme}={sessionCookie}");
        return await Record(await client.SendAsync(request, CancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        return await Record(await client.SendAsync(request, CancellationToken));
    }

    // Records the headers and body of a response for the secret check, and keeps the response readable.
    private async Task<HttpResponseMessage> Record(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        _serverResponses.Add($"{response.Headers}\n{response.Content.Headers}\n{body}");
        return response;
    }

    // No collected log entry, response header or body holds the OIDC client secret, and the stored secret is not plain text.
    private async Task AssertNoClientSecretAsync(LogCollector logs, string platform)
    {
        Assert.NotEmpty(logs.Entries);
        Assert.NotEmpty(_serverResponses);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(KeycloakFixture.ClientSecret, entry, StringComparison.Ordinal));
        Assert.All(_serverResponses, response => Assert.DoesNotContain(KeycloakFixture.ClientSecret, response, StringComparison.Ordinal));
        var secret = Encoding.UTF8.GetBytes(KeycloakFixture.ClientSecret);
        foreach (var id in new[] { "a", "b" })
        {
            var stored = (byte[])(await PostgreSqlServer.AdminScalarAsync(
                platform,
                $"SELECT oidc_client_secret FROM platform.tenants WHERE id = '{id}'",
                CancellationToken))!;
            Assert.False(stored.AsSpan().IndexOf(secret) >= 0, "The stored client secret holds the plain secret.");
        }
    }

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();

    private sealed record SignInResult(string SessionCookie);
}
