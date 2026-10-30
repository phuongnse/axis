using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Admin;
using Axis.Configuration.Tests;
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
/// An administrator also gives and removes an application role through <c>POST /api/users/{id}/give-role</c> and <c>remove-role</c>,
/// and the person's live session uses the change at its next request. That application, <c>Roles</c>, declares the role
/// <c>buyer</c> and a policy that lets it read notes. The same routes give and remove a platform role, which needs a login in the
/// last 5 minutes, and an administrator cannot remove their own administrator role.
/// The tenant is served at localhost.
/// </summary>
public sealed class UserCommandTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Administrator = "administrator";
    private const string Employee = "employee";
    private const string AuthenticatorSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string PlatformSecret = "platform-secret-1";
    private const string UsersSecret = "users-secret-1";

    private static readonly Guid _rolesId = Guid.Parse("5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c01");

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;
    private string _patId = null!;
    private string? _connectionString;
    private long _lastCodePeriod;
    private WebApplicationFactory<Program>? _factory;
    private TemporaryFolder? _rolesApplication;

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
        Assert.Equal("addAuthenticator", KeycloakSignIn.Page(html));
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
            using var problem = await RecordApiFixture.ReadProblemAsync(unauthorized, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
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

    [Fact]
    public async Task A_given_role_is_used_by_the_next_request_of_the_live_session_and_a_removed_role_is_not()
    {
        var pat = await SessionCookieAsync(_patId, Employee, authenticatedMinutesAgo: null);
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: null);
        Assert.Equal(HttpStatusCode.Forbidden, await RecordsStatusAsync(pat));

        using var given = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, Body("buyer"));

        Assert.Equal(HttpStatusCode.NoContent, given.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await RecordsStatusAsync(pat));
        Assert.Equal(1L, await GivenRolesCountAsync());
        Assert.Equal(0L, await SessionRowRoleCountAsync());

        using var removed = await PostJsonAsync($"/api/users/{_patId}/remove-role", admin, Body("buyer"));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await RecordsStatusAsync(pat));
        Assert.Equal(0L, await GivenRolesCountAsync());
    }

    [Fact]
    public async Task Giving_and_removing_a_role_each_write_one_audit_record_and_a_repeat_writes_none()
    {
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: null);

        using var first = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, Body("buyer"));
        using var repeated = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, Body("buyer"));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(1L, await RoleAuditCountAsync("user.roleGiven", "buyer"));
        Assert.Equal([Administrator], await AuditActorsAsync("user.roleGiven", _patId));

        using var removed = await PostJsonAsync($"/api/users/{_patId}/remove-role", admin, Body("buyer"));
        using var removedAgain = await PostJsonAsync($"/api/users/{_patId}/remove-role", admin, Body("buyer"));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedAgain.StatusCode);
        Assert.Equal(1L, await RoleAuditCountAsync("user.roleRemoved", "buyer"));
        Assert.Equal(1L, await RoleAuditCountAsync("user.roleGiven", "buyer"));
        Assert.Equal(2L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task A_person_who_is_not_an_administrator_gets_403_and_nobody_signed_in_gets_401_on_the_role_routes()
    {
        var employee = await SessionCookieAsync("someone", Employee, authenticatedMinutesAgo: 0);

        foreach (var command in new[] { "give-role", "remove-role" })
        {
            using var forbidden = await PostJsonAsync($"/api/users/{_patId}/{command}", employee, Body("buyer"));
            using var unauthorized = await PostJsonAsync($"/api/users/{_patId}/{command}", cookie: null, Body("buyer"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var problem = await RecordApiFixture.ReadProblemAsync(unauthorized, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(0L, await GivenRolesCountAsync());
        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task Giving_a_role_the_release_does_not_declare_or_that_is_no_platform_role_or_to_an_unknown_user_changes_and_audits_nothing()
    {
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: null);

        using var undeclared = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, Body("approver"));
        using var platform = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, """{ "role": "buyer" }""");
        using var blank = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, Body(" "));
        using var malformed = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, "{ not json");
        using var otherApplication = await PostJsonAsync(
            $"/api/users/{_patId}/give-role", admin, $$"""{ "applicationId": "{{Guid.NewGuid()}}", "role": "buyer" }""");
        using var unknown = await PostJsonAsync($"/api/users/{Guid.NewGuid()}/give-role", admin, Body("buyer"));
        using var unknownRemove = await PostJsonAsync($"/api/users/{Guid.NewGuid()}/remove-role", admin, Body("buyer"));
        using var platformRemove = await PostJsonAsync($"/api/users/{_patId}/remove-role", admin, """{ "role": "buyer" }""");

        Assert.Equal(HttpStatusCode.BadRequest, undeclared.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, platform.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otherApplication.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRemove.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, platformRemove.StatusCode);
        Assert.Equal(0L, await GivenRolesCountAsync());
        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task A_given_row_for_a_role_the_release_does_not_declare_grants_nothing_and_can_still_be_removed()
    {
        var pat = await SessionCookieAsync(_patId, Employee, authenticatedMinutesAgo: null);
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: null);
        await PostgreSqlServer.AdminScalarAsync(
            _connectionString!,
            $"INSERT INTO axis.given_roles (id, user_id, application_id, role) VALUES (gen_random_uuid(), '{_patId}', '{_rolesId}', 'dropped')",
            CancellationToken);

        using var removed = await PostJsonAsync($"/api/users/{_patId}/remove-role", admin, Body("dropped"));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0L, await GivenRolesCountAsync());
        Assert.Equal(1L, await RoleAuditCountAsync("user.roleRemoved", "dropped"));
        Assert.Equal(HttpStatusCode.Forbidden, await RecordsStatusAsync(pat));
    }

    [Fact]
    public async Task A_platform_role_change_with_an_old_login_answers_403_and_changes_and_audits_nothing()
    {
        var stale = await AdministratorCookieAsync(authenticatedMinutesAgo: 6);

        using var given = await PostJsonAsync($"/api/users/{_patId}/give-role", stale, PlatformBody("operator"));

        Assert.Equal(HttpStatusCode.Forbidden, given.StatusCode);
        Assert.Equal(0L, await GivenRolesCountAsync());

        await PostgreSqlServer.AdminScalarAsync(
            _connectionString!,
            $"INSERT INTO axis.given_roles (id, user_id, application_id, role) VALUES (gen_random_uuid(), '{_patId}', NULL, 'operator')",
            CancellationToken);

        using var removed = await PostJsonAsync($"/api/users/{_patId}/remove-role", stale, PlatformBody("operator"));

        Assert.Equal(HttpStatusCode.Forbidden, removed.StatusCode);
        Assert.Equal(1L, await GivenRolesCountAsync());
        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task An_administrator_removing_their_own_administrator_role_answers_409_and_changes_and_audits_nothing()
    {
        await PostgreSqlServer.AdminScalarAsync(
            _connectionString!,
            $"INSERT INTO axis.given_roles (id, user_id, application_id, role) VALUES (gen_random_uuid(), '{_patId}', NULL, 'administrator')",
            CancellationToken);
        var pat = await SessionCookieAsync(_patId, Administrator, authenticatedMinutesAgo: 0);

        using var removed = await PostJsonAsync($"/api/users/{_patId}/remove-role", pat, PlatformBody("administrator"));

        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
        Assert.Equal(1L, await GivenRolesCountAsync());
        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task A_given_platform_role_is_used_by_the_next_request_of_the_live_session_and_a_removed_one_is_not()
    {
        var pat = await SessionCookieAsync(_patId, Employee, authenticatedMinutesAgo: null);
        var admin = await AdministratorCookieAsync(authenticatedMinutesAgo: 0);
        Assert.False(await ConsoleAsync(pat));

        using var given = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, PlatformBody("operator"));
        using var repeated = await PostJsonAsync($"/api/users/{_patId}/give-role", admin, PlatformBody("operator"));

        Assert.Equal(HttpStatusCode.NoContent, given.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.True(await ConsoleAsync(pat));
        Assert.Equal(1L, await PlatformRoleAuditCountAsync("user.roleGiven", "operator"));

        using var removed = await PostJsonAsync($"/api/users/{_patId}/remove-role", admin, PlatformBody("operator"));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(await ConsoleAsync(pat));
        Assert.Equal(0L, await GivenRolesCountAsync());
        Assert.Equal(1L, await PlatformRoleAuditCountAsync("user.roleRemoved", "operator"));
        Assert.Equal(2L, await UserAuditCountAsync());
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
        _rolesApplication = RolesApplication();
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
            builder.UseSetting("Console:OperatorRole", "operator");
            builder.UseSetting("ActivateOnStartup:0", _rolesApplication.Path);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _rolesApplication?.Dispose();
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

    private static string PlatformBody(string role) => $$"""{ "role": "{{role}}" }""";

    private static string Body(string role) => $$"""{ "applicationId": "{{_rolesId}}", "role": "{{role}}" }""";

    /// <summary>The application <c>Roles</c> with the role <c>buyer</c>, the entity <c>Note</c> and a policy that lets <c>buyer</c> read notes.</summary>
    private static TemporaryFolder RolesApplication() => new TemporaryFolder()
        .With("application.json", $$"""{ "id": "{{_rolesId}}", "kind": "application", "name": "Roles", "formatVersion": 1 }""")
        .With("texts/en.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c10", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "role.buyer": "Buyer" } }
            """)
        .With("roles/buyer.json", """{ "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c13", "kind": "role", "name": "buyer", "formatVersion": 1, "label": { "textKey": "role.buyer" } }""")
        .With("entities/note.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c21", "kind": "entity", "name": "Note", "formatVersion": 1, "displayField": "title",
              "fields": [ { "name": "title", "type": "text", "required": true } ] }
            """)
        .With("policies/buyer-access.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c15", "kind": "policy", "name": "BuyerAccess", "formatVersion": 1,
              "roles": ["buyer"], "grants": [ { "entity": "Note", "actions": ["read"], "fields": ["title"] } ] }
            """);

    private async Task<HttpStatusCode> RecordsStatusAsync(string cookie)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/apps/Roles/entities/Note/records", UriKind.Relative));
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    private async Task<bool> ConsoleAsync(string cookie)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("console").GetBoolean();
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string path, string? cookie, string body)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<long> GivenRolesCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_connectionString!, "SELECT count(*) FROM axis.given_roles", CancellationToken))!;

    // A given role is never written to the session row, so the stored roles stay empty.
    private async Task<long> SessionRowRoleCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_connectionString!, "SELECT count(*) FROM axis.sessions WHERE application_roles <> '{}'::jsonb", CancellationToken))!;

    private async Task<long> RoleAuditCountAsync(string action, string role)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM axis.audit_records WHERE action = @action AND application_id = @application AND details->>'userId' = @user AND details->>'role' = @role",
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("application", _rolesId);
        command.Parameters.AddWithValue("user", _patId);
        command.Parameters.AddWithValue("role", role);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<long> PlatformRoleAuditCountAsync(string action, string role)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM axis.audit_records WHERE action = @action AND application_id IS NULL AND details->>'platform' = 'true' AND details->>'userId' = @user AND details->>'role' = @role",
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("user", _patId);
        command.Parameters.AddWithValue("role", role);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
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
