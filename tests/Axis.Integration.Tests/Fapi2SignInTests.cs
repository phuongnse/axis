using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Admin;
using Axis.Administration;
using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in through Keycloak under both sign-in profiles. Tenant <c>fapi</c> runs the <c>fapi2</c> profile at
/// <c>localhost</c> with the client <c>axis-fapi</c>, and tenant <c>plain</c> runs <c>standard</c> at <c>127.0.0.1</c>
/// with the client <c>axis</c>. Tenant <c>nopar</c> is a <c>fapi2</c> tenant whose provider metadata has no PAR
/// endpoint. Each tenant's calls to Keycloak are recorded, so the tests can show what the server sent.
/// </summary>
public sealed partial class Fapi2SignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string ParPath = "/protocol/openid-connect/ext/par/request";
    private const string TokenPath = "/protocol/openid-connect/token";
    private const string NoParHost = "nopar.example.test";

    // axis-fapi accepts redirects to http://localhost:5206/*, and axis also accepts http://127.0.0.1:5280/*.
    private static readonly Uri _fapiAddress = new("http://localhost:5206");
    private static readonly Uri _plainAddress = new("http://127.0.0.1:5280");

    private static readonly string[] _tenantIds = ["fapi", "plain", "nopar"];

    private static readonly string[] _fapi2TenantIds = ["fapi", "nopar"];

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fapi2_tenant_signs_in_through_par_with_a_client_assertion_and_no_client_secret()
    {
        var authority = await keycloak.StartAsync();
        var (factory, calls, _, _) = await StartConfigurationHostAsync(authority);
        await using var _ = factory;
        using var client = CreateClient(factory, _fapiAddress);

        var callback = await LogInAtKeycloakAsync(client, authority, "requester", _fapiAddress, par: true);
        using var signedIn = await SendAsync(client, callback.PathAndQuery);

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        Assert.Equal("requester", await MeAsync(client));
        var fapi = calls["fapi"].Calls;
        var par = Assert.Single(fapi, call => call.Url.EndsWith(ParPath, StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post, par.Method);
        Assert.Null(par.Dpop);
        Assert.Equal("28800", par.Form["max_age"]);
        Assert.Equal("S256", par.Form["code_challenge_method"]);
        Assert.Equal(KeycloakFixture.FapiClientId, par.Form["client_id"]);
        Assert.Equal(ClientAssertion.Type, par.Form["client_assertion_type"]);
        Assert.NotEmpty(par.Form["client_assertion"]);
        var token = Assert.Single(fapi, call => call.Url.EndsWith(TokenPath, StringComparison.Ordinal));
        Assert.NotEmpty(token.Form["client_assertion"]);
        Assert.NotEqual(par.Form["client_assertion"], token.Form["client_assertion"]);
        Assert.NotNull(token.Dpop);
        var proof = new JsonWebToken(token.Dpop);
        Assert.Equal("dpop+jwt", proof.Typ);
        Assert.Equal("POST", proof.GetPayloadValue<string>("htm"));
        Assert.Equal(token.Url, proof.GetPayloadValue<string>("htu"));
        Assert.All(fapi.Where(call => call.Form.Count > 0), call => Assert.DoesNotContain("client_secret", call.Form.Keys));
    }

    [Fact]
    public async Task A_standard_tenant_signs_in_without_par_and_without_a_client_assertion()
    {
        var authority = await keycloak.StartAsync();
        var (factory, calls, _, _) = await StartConfigurationHostAsync(authority);
        await using var _ = factory;
        using var client = CreateClient(factory, _plainAddress);

        var callback = await LogInAtKeycloakAsync(client, authority, "binh", _plainAddress, par: false);
        using var signedIn = await SendAsync(client, WithIssuer(callback, issuer: null).PathAndQuery);

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal("binh", await MeAsync(client));
        var plain = calls["plain"].Calls;
        Assert.DoesNotContain(plain, call => call.Url.EndsWith(ParPath, StringComparison.Ordinal));
        Assert.DoesNotContain(plain, call => call.Form.ContainsKey("client_assertion"));
        var token = Assert.Single(plain, call => call.Url.EndsWith(TokenPath, StringComparison.Ordinal));
        Assert.Equal(KeycloakFixture.ClientSecret, token.Form["client_secret"]);
        Assert.Null(token.Dpop);
    }

    [Theory]
    [InlineData("https://attacker.example.test/realms/axis")]
    [InlineData(null)]
    public async Task A_fapi2_callback_with_a_wrong_or_missing_iss_is_refused_before_the_code_is_redeemed(string? issuer)
    {
        var authority = await keycloak.StartAsync();
        var (factory, calls, _, databases) = await StartConfigurationHostAsync(authority);
        await using var _ = factory;
        using var client = CreateClient(factory, _fapiAddress);
        var callback = await LogInAtKeycloakAsync(client, authority, "requester", _fapiAddress, par: true);

        using var refused = await SendAsync(client, WithIssuer(callback, issuer).PathAndQuery);

        Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
        Assert.Equal("/?signIn=failed", refused.Headers.Location?.OriginalString);
        using var me = await SendAsync(client, "/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(["invalidIssuer"], await RefusalReasonsAsync(databases["fapi"]));
        Assert.DoesNotContain(calls["fapi"].Calls, call => call.Url.EndsWith(TokenPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_directory_tenant_signs_every_client_assertion_with_its_newest_key()
    {
        var authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("fapi2_platform", CancellationToken);
        var tenantDatabase = await CreateTenantDatabaseAsync("fapi2_directory");
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
            var connection = new NpgsqlConnectionStringBuilder(tenantDatabase);
            context.Tenants.Add(new TenantRow
            {
                Id = "d",
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = connection.Host!,
                DatabasePort = connection.Port,
                DatabaseName = connection.Database!,
                RuntimeUser = PostgreSqlServer.RuntimeRole,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt("d", TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
                SchemaPassword = _cipher.Encrypt("d", TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = authority.ToString(),
                OidcClientId = KeycloakFixture.FapiClientId,
                OidcClientSecret = [],
                OidcProfile = TenantSignInProfiles.Fapi2,
                OidcClientKeyId = "key-2",
                OidcClientKey = _cipher.Encrypt("d", TenantSecretFields.OidcClientKey, KeycloakFixture.FapiClientKey("key-2")),
                OidcPreviousClientKeyId = "key-1",
                OidcPreviousClientKey = _cipher.Encrypt("d", TenantSecretFields.OidcClientKey, KeycloakFixture.FapiClientKey("key-1")),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = "d", Host = "localhost" });
            await context.SaveChangesAsync(CancellationToken);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        var recorder = new RecordingHandler();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(platform);
            builder.ConfigureTestServices(services =>
                services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName("d"), options => options.BackchannelHttpHandler = recorder));
        });
        using var client = CreateClient(factory, _fapiAddress);

        var callback = await LogInAtKeycloakAsync(client, authority, "requester", _fapiAddress, par: true);
        using var signedIn = await SendAsync(client, callback.PathAndQuery);

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal("requester", await MeAsync(client));
        var assertions = recorder.Calls.Where(call => call.Form.ContainsKey("client_assertion")).Select(call => call.Form["client_assertion"]).ToList();
        Assert.True(assertions.Count >= 2, "The PAR request and the token request each carry an assertion.");
        Assert.All(assertions, assertion => Assert.Equal("key-2", new JsonWebToken(assertion).Kid));
    }

    [Fact]
    public async Task A_client_key_added_by_the_command_signs_at_the_next_refresh_and_sign_in_keeps_working_after_the_old_key_is_retired()
    {
        var authority = await keycloak.StartAsync();
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "localhost")).Exit);
        Assert.Equal(
            0,
            (await scenario.RunAsync(
                ["tenant", "oidc", "set", id, "--authority", authority.ToString(), "--client-id", KeycloakFixture.FapiClientId],
                extra: AdminScenario.Directory)).Exit);
        var oldKey = KeycloakFixture.FapiClientKey("key-1");
        var newKey = KeycloakFixture.FapiClientKey("key-2");
        Assert.True(TenantClientKeys.TryRead(oldKey, out _, out var oldKeyId, out _));
        Assert.True(TenantClientKeys.TryRead(newKey, out _, out var newKeyId, out _));

        // The realm lists each key under its thumbprint as well, so Keycloak accepts the key ids that the command prints.
        Assert.Equal("c78k1xDMCWYQ6xjENt1SIlXg-51NnvHQaVaO5rwsk4k", oldKeyId);
        Assert.Equal("9WHVOfd_bSEKHv5990xfBx9M4tKFIPnKDyERLW9seMI", newKeyId);
        var recorder = new RecordingHandler();
        var logs = new LogCollector();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(scenario.Platform);

            // An hour, so the timer never races the refreshes that the test triggers itself.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            builder.ConfigureTestServices(services =>
                services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName(id), options => options.BackchannelHttpHandler = recorder));
        });
        var refresh = factory.Services.GetRequiredService<TenantDirectoryRefresh>();

        // The first key is added before the host reads the tenant, so the server signs with it.
        Assert.Equal(0, (await SetKeyAsync(scenario, id, oldKey, "--profile", "fapi2")).Exit);
        await refresh.RefreshAsync(CancellationToken);
        await SignInAndAssertKeyAsync(factory, authority, recorder, oldKeyId);

        // The second key is the newest, so after the next refresh every assertion carries its key id.
        Assert.Equal(0, (await SetKeyAsync(scenario, id, newKey)).Exit);
        await refresh.RefreshAsync(CancellationToken);
        await SignInAndAssertKeyAsync(factory, authority, recorder, newKeyId);

        var retired = await scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--retire-client-key", oldKeyId],
            extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory" });
        Assert.Equal(0, retired.Exit);
        await refresh.RefreshAsync(CancellationToken);
        await SignInAndAssertKeyAsync(factory, authority, recorder, newKeyId);

        foreach (var key in new[] { oldKey, newKey })
        {
            Assert.DoesNotContain(logs.Entries, entry => AdminScenario.HoldsPemBody(entry, key));
        }
    }

    [Fact]
    public async Task A_fapi2_tenant_whose_provider_offers_no_par_endpoint_redirects_to_the_failed_page_and_logs_a_warning()
    {
        var authority = await keycloak.StartAsync();
        var (factory, calls, logs, _) = await StartConfigurationHostAsync(authority);
        await using var _ = factory;
        using var client = CreateClient(factory, _fapiAddress);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/sign-in", UriKind.Relative));
        request.Headers.Host = NoParHost;

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/?signIn=failed", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(calls["nopar"].Calls, call => call.Url.EndsWith(ParPath, StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("nopar", StringComparison.Ordinal));
    }

    private static Task<AdminResult> SetKeyAsync(AdminScenario scenario, string id, string pem, params string[] more) =>
        scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--client-key", .. more],
            extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory", [AdminCommands.ClientKeySetting] = pem });

    // Signs in with a new client and checks that every client assertion of that sign-in carries the key id.
    private static async Task SignInAndAssertKeyAsync(WebApplicationFactory<Program> factory, Uri authority, RecordingHandler recorder, string keyId)
    {
        var before = recorder.Calls.Count;
        using var client = CreateClient(factory, _fapiAddress);

        var callback = await LogInAtKeycloakAsync(client, authority, "requester", _fapiAddress, par: true);
        using var signedIn = await SendAsync(client, callback.PathAndQuery);

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal("requester", await MeAsync(client));
        var assertions = recorder.Calls.Skip(before).Where(call => call.Form.ContainsKey("client_assertion")).Select(call => call.Form["client_assertion"]).ToList();
        Assert.True(assertions.Count >= 2, "The PAR request and the token request each carry an assertion.");
        Assert.All(assertions, assertion => Assert.Equal(keyId, new JsonWebToken(assertion).Kid));
    }

    private async Task<string> CreateTenantDatabaseAsync(string prefix)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        await configuration.Database.MigrateAsync(CancellationToken);
        await data.Database.MigrateAsync(CancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }

    /// <summary>
    /// Starts a host with the configuration tenants <c>fapi</c>, <c>plain</c> and <c>nopar</c>, and records each
    /// tenant's calls to Keycloak. Also returns the schema connection string of each tenant's database.
    /// </summary>
    private async Task<(WebApplicationFactory<Program> Factory, Dictionary<string, RecordingHandler> Calls, LogCollector Logs, Dictionary<string, string> Databases)> StartConfigurationHostAsync(Uri authority)
    {
        var databases = new Dictionary<string, string>();
        foreach (var id in _tenantIds)
        {
            databases[id] = await CreateTenantDatabaseAsync($"fapi2_{id}");
        }

        var calls = new Dictionary<string, RecordingHandler>
        {
            ["fapi"] = new(),
            ["plain"] = new(),
            ["nopar"] = new(removePar: true),
        };
        var logs = new LogCollector();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(databases["fapi"]);
            foreach (var (id, host) in new[] { ("fapi", "localhost"), ("plain", "127.0.0.1"), ("nopar", NoParHost) })
            {
                builder.UseSetting($"Tenants:{id}:Hosts:0", host);
                builder.UseSetting($"Tenants:{id}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(databases[id]));
                builder.UseSetting($"Tenants:{id}:SchemaConnectionString", databases[id]);
                builder.UseSetting($"Tenants:{id}:Oidc:Authority", authority.ToString());
            }

            // The fapi2 tenants have a client secret too, to show that the server does not send it.
            foreach (var id in _fapi2TenantIds)
            {
                builder.UseSetting($"Tenants:{id}:Oidc:ClientId", KeycloakFixture.FapiClientId);
                builder.UseSetting($"Tenants:{id}:Oidc:ClientSecret", "must-not-be-sent");
                builder.UseSetting($"Tenants:{id}:Oidc:Profile", TenantSignInProfiles.Fapi2);
                builder.UseSetting($"Tenants:{id}:Oidc:ClientKey", KeycloakFixture.FapiClientKey("key-1"));
                builder.UseSetting($"Tenants:{id}:Oidc:ClientKeyId", "key-1");
            }

            builder.UseSetting("Tenants:plain:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:plain:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            // This runs after the server's own options, so the OIDC post-configuration builds the backchannel from the recorder.
            builder.ConfigureTestServices(services =>
            {
                foreach (var (id, recorder) in calls)
                {
                    services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName(id), options => options.BackchannelHttpHandler = recorder);
                }
            });
        });
        return (factory, calls, logs, databases);
    }

    /// <summary>The reasons of the <c>session.signInRefused</c> records in a tenant database, oldest first.</summary>
    private static async Task<List<string>> RefusalReasonsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT details->>'reason' FROM axis.audit_records WHERE action = 'session.signInRefused' ORDER BY occurred_at, id",
            connection);
        var reasons = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            reasons.Add(reader.GetString(0));
        }

        return reasons;
    }

    /// <summary>The callback address with its <c>iss</c> replaced by <paramref name="issuer"/>, or removed when that is null.</summary>
    private static Uri WithIssuer(Uri callback, string? issuer)
    {
        var parameters = QueryHelpers.ParseQuery(callback.Query)
            .Where(pair => pair.Key != "iss")
            .Select(pair => KeyValuePair.Create(pair.Key, (string?)pair.Value.ToString()))
            .ToList();
        if (issuer is not null)
        {
            parameters.Add(KeyValuePair.Create("iss", (string?)issuer));
        }

        return new Uri(QueryHelpers.AddQueryString(callback.GetLeftPart(UriPartial.Path), parameters));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, Uri address) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = address,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>
    /// Starts sign-in on the server and logs in at Keycloak's login form as a browser would. Returns the callback
    /// address that Keycloak sends the browser to, which has not been called. Under PAR the redirect holds only the
    /// client id and the <c>request_uri</c>, so the PKCE parameters are not in it.
    /// </summary>
    private static async Task<Uri> LogInAtKeycloakAsync(HttpClient client, Uri authority, string username, Uri expectedBase, bool par)
    {
        using var challenge = await SendAsync(client, "/api/auth/sign-in");
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith($"{authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
        if (par)
        {
            Assert.Contains("request_uri=", authorize.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("code_challenge", authorize.Query, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("request_uri=", authorize.Query, StringComparison.Ordinal);
            Assert.Contains("code_challenge_method=S256", authorize.Query, StringComparison.Ordinal);
        }

        // A separate client plays the browser at Keycloak. Keycloak marks its cookies Secure, which a
        // browser sends to http://localhost but a CookieContainer does not, so the client carries them itself.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        // Under PAR Keycloak first redirects to its authenticate action, and the browser carries the cookies along.
        var cookies = new Dictionary<string, string>();
        var loginPage = await GetAsync(browser, authorize, cookies);
        while (loginPage.StatusCode == HttpStatusCode.Found)
        {
            loginPage = await GetAsync(browser, loginPage.Headers.Location!, cookies);
        }

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
        loginRequest.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        var login = await browser.SendAsync(loginRequest, CancellationToken);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal($"{expectedBase.GetLeftPart(UriPartial.Authority)}/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        return callback;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient browser, Uri address, Dictionary<string, string> cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        var response = await browser.SendAsync(request, CancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                var pair = setCookie.Split(';')[0].Split('=', 2);
                cookies[pair[0]] = pair[1];
            }
        }

        return response;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<string?> MeAsync(HttpClient client)
    {
        using var me = await SendAsync(client, "/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetString();
    }

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();

    /// <summary>One request that the server sent to Keycloak: its method, address, form fields and <c>DPoP</c> header.</summary>
    private sealed record RecordedCall(HttpMethod Method, string Url, Dictionary<string, string> Form, string? Dpop);

    /// <summary>
    /// Sends the server's calls to Keycloak and records them. With <paramref name="removePar"/> the provider metadata
    /// loses its PAR endpoint, as a provider without PAR would answer.
    /// </summary>
    private sealed class RecordingHandler(bool removePar = false) : DelegatingHandler(new HttpClientHandler())
    {
        private readonly ConcurrentQueue<RecordedCall> _calls = new();

        public ConcurrentQueue<RecordedCall> Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = new Dictionary<string, string>();
            if (request.Content is { } content && content.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded")
            {
                foreach (var pair in (await content.ReadAsStringAsync(cancellationToken)).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    form[WebUtility.UrlDecode(parts[0])] = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : "";
                }
            }

            var dpop = request.Headers.TryGetValues("DPoP", out var proofs) ? proofs.Single() : null;
            _calls.Enqueue(new RecordedCall(request.Method, request.RequestUri!.ToString(), form, dpop));
            var response = await base.SendAsync(request, cancellationToken);
            if (removePar && request.RequestUri.AbsolutePath.EndsWith(".well-known/openid-configuration", StringComparison.Ordinal))
            {
                var metadata = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
                metadata.Remove("pushed_authorization_request_endpoint");
                response.Content = new StringContent(metadata.ToJsonString(), System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            }

            return response;
        }
    }
}
