using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Admin;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// An administrator disables a person, enables them again and resets an account's authenticator through
/// <c>POST /api/users/{id}/disable</c>, <c>enable</c> and <c>reset-authenticator</c>. The person signs in for real at a realm that
/// <see cref="TenantRealm"/> makes in a real Keycloak, with a password and an authenticator, and the server changes the realm
/// through that realm's <c>axis-users</c> client. A test-only path signs the administrator in with the login time and roles that a test asks for.
/// The tenant is served at localhost.
/// </summary>
public sealed class UserCommandTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Administrator = "administrator";
    private const string Employee = "employee";
    private const string AuthenticatorSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string PlatformSecret = "platform-secret-1";
    private const string UsersSecret = "users-secret-1";

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;
    private string _patId = null!;
    private string? _connectionString;
    private long _lastCodePeriod;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task Disabling_a_person_ends_their_sessions_and_refuses_sign_in_and_enabling_restores_it_with_the_same_roles()
    {
        using var pat = await SignInAsync();
        var rolesBefore = await RolesAsync(pat);
        Assert.Contains(Employee, rolesBefore);
        Assert.NotEmpty(await RealmSessionsAsync());
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: 0);

        using var disabled = await PostAsync($"/api/users/{_patId}/disable", admin);

        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using (var me = await pat.GetAsync("/api/me", CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        Assert.Empty(await RealmSessionsAsync());
        Assert.Equal(0L, await SessionRowsAsync(_patId));
        Assert.False(await RealmEnabledAsync());
        using (var refused = CreateClient(cookies: true))
        {
            var (_, afterPassword) = await StartLoginAsync(refused);
            Assert.NotEqual(HttpStatusCode.Found, afterPassword.StatusCode);
            Assert.DoesNotContain("\"twoStep\"", await afterPassword.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        }

        Assert.Equal([Administrator], await AuditActorsAsync("user.disabled", _patId));

        using var enabled = await PostAsync($"/api/users/{_patId}/enable", admin);

        Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
        Assert.True(await RealmEnabledAsync());
        Assert.Contains("otp", await CredentialTypesAsync());
        using var again = await SignInAsync();
        Assert.Equal(rolesBefore.Order(StringComparer.Ordinal), (await RolesAsync(again)).Order(StringComparer.Ordinal));
        Assert.Equal([Administrator], await AuditActorsAsync("user.disabled", _patId));
        Assert.Equal([Administrator], await AuditActorsAsync("user.enabled", _patId));
    }

    [Fact]
    public async Task Resetting_the_authenticator_ends_the_sessions_and_the_next_sign_in_asks_for_a_new_authenticator()
    {
        using var pat = await SignInAsync();
        Assert.Contains("otp", await CredentialTypesAsync());

        // Resetting needs no recent login, unlike disabling.
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: 6);

        using var reset = await PostAsync($"/api/users/{_patId}/reset-authenticator", admin);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using (var me = await pat.GetAsync("/api/me", CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        Assert.Empty(await RealmSessionsAsync());
        Assert.Equal(0L, await SessionRowsAsync(_patId));
        Assert.DoesNotContain("otp", await CredentialTypesAsync());
        Assert.True(await RealmEnabledAsync());
        using var next = CreateClient(cookies: true);
        var (browser, afterPassword) = await StartLoginAsync(next);
        Assert.Equal(HttpStatusCode.Found, afterPassword.StatusCode);
        using var page = await browser.GetAsync(afterPassword.Headers.Location!);
        var html = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("name=\"totp\"", html, StringComparison.Ordinal);
        Assert.Equal([Administrator], await AuditActorsAsync("user.authenticatorReset", _patId));
    }

    [Fact]
    public async Task Disabling_with_an_old_login_or_oneself_is_refused_and_changes_and_audits_nothing()
    {
        using var pat = await SignInAsync();
        var stale = await AdministratorCookieAsync(authenticatedMinutesAgo: 6);
        var recent = await AdministratorCookieAsync(authenticatedMinutesAgo: 0);
        var unknown = await AdministratorCookieAsync(authenticatedMinutesAgo: null);

        using var old = await PostAsync($"/api/users/{_patId}/disable", stale);
        using var self = await PostAsync($"/api/users/{Administrator}/disable", recent);
        using var noLoginTime = await PostAsync($"/api/users/{_patId}/disable", unknown);
        using var missing = await PostAsync($"/api/users/{Guid.NewGuid()}/disable", recent);

        Assert.Equal(HttpStatusCode.Forbidden, old.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noLoginTime.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.True(await RealmEnabledAsync());
        using (var me = await pat.GetAsync("/api/me", CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task A_person_who_is_not_an_administrator_gets_403_and_nobody_signed_in_gets_401_on_all_three_routes()
    {
        using var pat = await SignInAsync();
        var employee = await SessionCookieAsync("someone", Employee, authenticatedMinutesAgo: 0);

        foreach (var command in new[] { "disable", "enable", "reset-authenticator" })
        {
            using var forbidden = await PostAsync($"/api/users/{_patId}/{command}", employee);
            using var unauthorized = await PostAsync($"/api/users/{_patId}/{command}", cookie: null);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        Assert.True(await RealmEnabledAsync());
        using var me = await pat.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task A_service_account_of_the_realm_answers_404_to_every_command()
    {
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: 0);
        var serviceAccount = await ServiceAccountIdAsync();

        foreach (var command in new[] { "disable", "enable", "reset-authenticator" })
        {
            using var response = await PostAsync($"/api/users/{serviceAccount}/{command}", admin);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(0L, await UserAuditCountAsync());
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, "activation-secret-1", UsersSecret), CancellationToken);
        await ConfigureClientAsync();
        _patId = await CreatePatAsync();

        _connectionString = await database.CreateDatabaseAsync("user_commands", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_connectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseSetting("Tenants:a:Oidc:Authority", new Uri(_baseAddress, $"realms/{_realm}").ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", "axis");
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", PlatformSecret);
            builder.UseSetting("Tenants:a:Oidc:UsersClientSecret", UsersSecret);
            builder.UseSetting("Tenants:a:Oidc:RolesClaim", "roles");
            builder.UseSetting("Users:AdministratorRole", Administrator);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{_realm}");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    // The axis client accepts the test host's callback, and puts the realm roles of a person in its roles claim.
    private async Task ConfigureClientAsync()
    {
        var axis = (await GetJsonAsync($"admin/realms/{_realm}/clients?clientId=axis")).EnumerateArray().Single();
        var uuid = axis.GetProperty("id").GetString()!;
        var representation = JsonNode.Parse(axis.GetRawText())!.AsObject();
        representation["redirectUris"] = new JsonArray("http://localhost/*");
        using var updated = await SendAsync(HttpMethod.Put, $"admin/realms/{_realm}/clients/{uuid}", representation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        using var mapper = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/clients/{uuid}/protocol-mappers/models", new
        {
            name = "roles",
            protocol = "openid-connect",
            protocolMapper = "oidc-usermodel-realm-role-mapper",
            config = new Dictionary<string, string>
            {
                ["claim.name"] = "roles",
                ["multivalued"] = "true",
                ["jsonType.label"] = "String",
                ["id.token.claim"] = "true",
                ["access.token.claim"] = "true",
                ["userinfo.token.claim"] = "true",
            },
        });
        Assert.Equal(HttpStatusCode.Created, mapper.StatusCode);
    }

    // The account signs in with a password and a code from an authenticator, and holds the realm role employee.
    private async Task<string> CreatePatAsync()
    {
        using var role = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/roles", new { name = Employee });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users", new
        {
            username = "pat",
            enabled = true,
            emailVerified = true,
            email = "pat@example.test",
            firstName = "Pat",
            lastName = "Example",
            credentials = new object[]
            {
                new { type = "password", value = _password, temporary = false },
                new
                {
                    type = "otp",
                    secretData = $$"""{"value":"{{AuthenticatorSecret}}"}""",
                    credentialData = """{"subType":"totp","digits":6,"period":30,"algorithm":"HmacSHA1","counter":0,"secretEncoding":"BASE32"}""",
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = created.Headers.Location!.Segments[^1];
        var realmRole = await GetJsonAsync($"admin/realms/{_realm}/roles/{Employee}");
        using var mapped = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users/{id}/role-mappings/realm", new[]
        {
            new { id = realmRole.GetProperty("id").GetString(), name = Employee },
        });
        Assert.Equal(HttpStatusCode.NoContent, mapped.StatusCode);
        return id;
    }

    private async Task<string> ServiceAccountIdAsync()
    {
        var users = (await GetJsonAsync($"admin/realms/{_realm}/clients?clientId=axis-users")).EnumerateArray().Single();
        var account = await GetJsonAsync($"admin/realms/{_realm}/clients/{users.GetProperty("id").GetString()}/service-account-user");
        return account.GetProperty("id").GetString()!;
    }

    private HttpClient CreateClient(bool cookies) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = cookies,
        });

    /// <summary>Signs pat in through the server and the realm's login form and returns the client that holds the session.</summary>
    private async Task<HttpClient> SignInAsync()
    {
        var app = CreateClient(cookies: true);
        var (browser, afterPassword) = await StartLoginAsync(app);
        var html = await afterPassword.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(afterPassword.StatusCode == HttpStatusCode.OK && KeycloakSignIn.Page(html) == "twoStep", html);
        using var login = await browser.PostAsync(
            KeycloakSignIn.LoginAction(html).AbsoluteUri,
            new Dictionary<string, string>
            {
                ["otp"] = await NextAuthenticatorCodeAsync(),
                ["login"] = "Sign In",
            });
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal("http://localhost/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        using var signedIn = await app.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        return app;
    }

    // The realm accepts only the code of the current 30 seconds and refuses one it has seen, so a second sign-in waits for the next period.
    private async Task<string> NextAuthenticatorCodeAsync()
    {
        while (true)
        {
            var now = DateTimeOffset.UtcNow;
            var period = now.ToUnixTimeSeconds() / 30;
            if (period > _lastCodePeriod && now.ToUnixTimeSeconds() % 30 < 27)
            {
                _lastCodePeriod = period;
                return DevelopmentTotp.CodeFromSecret(AuthenticatorSecret, now);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        }
    }

    /// <summary>Starts sign-in on the server and posts pat's password at the realm's login form. Returns the browser and the response to the password.</summary>
    private async Task<(RealmBrowser Browser, HttpResponseMessage AfterPassword)> StartLoginAsync(HttpClient app)
    {
        using var challenge = await app.GetAsync("/api/auth/sign-in", CancellationToken);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.Contains($"/realms/{_realm}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
        var browser = new RealmBrowser();
        using var page = await browser.GetAsync(authorize);
        var html = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        var afterPassword = await browser.PostAsync(
            KeycloakSignIn.LoginAction(html).AbsoluteUri,
            new Dictionary<string, string> { ["username"] = "pat", ["password"] = _password, ["credentialId"] = "" });
        return (browser, afterPassword);
    }

    private static async Task<string[]> RolesAsync(HttpClient pat)
    {
        using var me = await pat.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return [.. body.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!)];
    }

    private async Task<JsonElement[]> RealmSessionsAsync() =>
        [.. (await GetJsonAsync($"admin/realms/{_realm}/users/{_patId}/sessions")).EnumerateArray()];

    private async Task<bool> RealmEnabledAsync() => (await GetJsonAsync($"admin/realms/{_realm}/users/{_patId}")).GetProperty("enabled").GetBoolean();

    private async Task<string[]> CredentialTypesAsync() =>
        [.. (await GetJsonAsync($"admin/realms/{_realm}/users/{_patId}/credentials")).EnumerateArray().Select(credential => credential.GetProperty("type").GetString()!)];

    private Task<string> AdministratorCookieAsync(int? authenticatedMinutesAgo) =>
        SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo);

    /// <summary>Signs a user in through the test-only path, with a last login <paramref name="authenticatedMinutesAgo"/> minutes ago or none, and returns the session cookie's value.</summary>
    private async Task<string> SessionCookieAsync(string user, string role, int? authenticatedMinutesAgo)
    {
        using var client = CreateClient(cookies: false);
        var query = authenticatedMinutesAgo is { } minutes ? $"&authenticatedMinutesAgo={minutes}" : string.Empty;
        using var response = await client.GetAsync(new Uri($"/test/session?user={user}&role={role}{query}", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string? cookie)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Axis-Csrf", "1");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<List<string>> AuditActorsAsync(string action, string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT actor FROM axis.audit_records WHERE action = @action AND details->>'userId' = @user", connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("user", userId);
        var actors = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            actors.Add(reader.GetString(0));
        }

        return actors;
    }

    private async Task<long> UserAuditCountAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.audit_records WHERE action LIKE 'user.%'", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<long> SessionRowsAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.sessions WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, CancellationToken);
    }

    private async Task<string> MasterTokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// The browser at the realm. Keycloak marks its cookies Secure, which a browser sends to http://localhost but a
    /// CookieContainer does not, so the browser carries them itself.
    /// </summary>
    private sealed class RealmBrowser : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        private readonly Dictionary<string, string> _jar = [];

        public async Task<HttpResponseMessage> GetAsync(Uri address)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            return await SendAsync(request);
        }

        public async Task<HttpResponseMessage> PostAsync(string address, Dictionary<string, string> form)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address)) { Content = new FormUrlEncodedContent(form) };
            return await SendAsync(request);
        }

        public void Dispose() => _client.Dispose();

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _jar.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            var response = await _client.SendAsync(request, CancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var pair in setCookies.Select(setCookie => setCookie.Split(';')[0].Split('=', 2)))
                {
                    _jar[pair[0]] = pair[1];
                }
            }

            return response;
        }
    }

    /// <summary>
    /// Answers <c>GET /test/session?user=&amp;role=&amp;authenticatedMinutesAgo=</c> by signing the user in with that role and a last login
    /// at the identity provider that many minutes ago, or with none when the minutes are missing.
    /// </summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The session store reads the tenant, which the real pipeline resolves only after this filter.
            app.UseWhen(
                context => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session",
                branch =>
                {
                    branch.UseTenantResolution();
                    branch.Run(async context =>
                    {
                        var tenantId = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current!.TenantId;
                        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
                        var user = context.Request.Query["user"].ToString();
                        DateTimeOffset? authenticatedAt = int.TryParse(context.Request.Query["authenticatedMinutesAgo"], out var minutes)
                            ? clock.GetUtcNow() - TimeSpan.FromMinutes(minutes)
                            : null;
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenantId, user, user, [context.Request.Query["role"].ToString()], authenticatedAt));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
