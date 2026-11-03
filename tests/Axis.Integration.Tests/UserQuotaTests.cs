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
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// In a cell, the invite and enable routes of the users page refuse a change that would pass the tenant's users quota, see
/// <c>docs/reference/tenant-quotas.md#refusals</c>. The realm is one that <see cref="TenantRealm"/> makes in a real Keycloak, with a
/// quota of 2 users. The count is the realm's enabled users, so a disabled user and the realm's service accounts do not count. At the
/// quota an invite and the enable of a disabled user answer 403 <c>quota-exceeded</c> and change nothing, an existing account still
/// signs in, and below the quota both succeed. The host replaces its quotas with a cell's over the configured tenant, because a full
/// directory-backed cell would make the test much heavier, and the path from the directory to the limit has its own tests.
/// </summary>
public sealed class UserQuotaTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
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
    private string _danaId = null!;
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task Below_the_quota_an_invite_succeeds_and_a_disabled_user_and_the_service_accounts_do_not_count()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator);
        var email = NewEmail();

        using var invited = await InviteAsync(admin, email);

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        Assert.Single((await GetJsonAsync($"admin/realms/{_realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray());
    }

    [Fact]
    public async Task Below_the_quota_enabling_a_disabled_user_succeeds()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator);

        using var enabled = await PostAsync($"/api/users/{_danaId}/enable", admin);

        Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
        Assert.True(await IsEnabledAsync(_danaId));
        Assert.Equal(1L, await EnabledAuditCountAsync());
    }

    [Fact]
    public async Task At_the_quota_an_invite_answers_403_even_for_a_malformed_body_creates_nothing_and_a_non_administrator_gets_a_plain_403()
    {
        await CreateSecondEnabledUserAsync();
        var admin = await SessionCookieAsync(Administrator, Administrator);
        var employee = await SessionCookieAsync(Employee, Employee);
        var email = NewEmail();

        using var refused = await InviteAsync(admin, email);
        using var malformed = await PostJsonAsync("/api/users/invite", admin, "{ not json");
        using var forbidden = await InviteAsync(employee, email);

        foreach (var response in new[] { refused, malformed })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal("quota-exceeded", problem.GetProperty("reason").GetString());
            Assert.Equal("users", problem.GetProperty("quota").GetString());
            Assert.Equal(2, problem.GetProperty("limit").GetInt64());
            Assert.Equal(2, problem.GetProperty("used").GetInt64());
            Assert.Equal("This tenant has reached its users quota.", problem.GetProperty("title").GetString());
        }

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var plain = await forbidden.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.False(plain.TryGetProperty("reason", out _));
        Assert.Empty((await GetJsonAsync($"admin/realms/{_realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray());
        Assert.Empty(await mailpit.MessagesToAsync(email, CancellationToken));
        Assert.Equal(0L, await InvitedAuditCountAsync());
    }

    [Fact]
    public async Task At_the_quota_enabling_a_disabled_user_answers_403_and_the_user_stays_disabled_and_enabling_an_enabled_user_still_works()
    {
        await CreateSecondEnabledUserAsync();
        var admin = await SessionCookieAsync(Administrator, Administrator);

        using var refused = await PostAsync($"/api/users/{_danaId}/enable", admin);
        using var already = await PostAsync($"/api/users/{await PatIdAsync()}/enable", admin);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("quota-exceeded", problem.GetProperty("reason").GetString());
        Assert.Equal("users", problem.GetProperty("quota").GetString());
        Assert.False(await IsEnabledAsync(_danaId));
        Assert.Equal(HttpStatusCode.NoContent, already.StatusCode);
        Assert.Equal(1L, await EnabledAuditCountAsync());
    }

    [Fact]
    public async Task At_the_quota_an_existing_account_still_signs_in()
    {
        await CreateSecondEnabledUserAsync();
        using var app = CreateClient(cookies: true);

        await new InvitationFlow().SignInAsync(app, "pat", _password, AuthenticatorSecret);

        using var me = await app.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        var smtpServer = new Dictionary<string, string>
        {
            ["host"] = MailpitFixture.NetworkAlias,
            ["port"] = $"{MailpitFixture.NetworkSmtpPort}",
            ["from"] = "noreply@example.test",
            ["starttls"] = "false",
        };
        await mailpit.SmtpHostAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, "activation-secret-1", UsersSecret), smtpServer, CancellationToken);
        await ConfigureClientAsync();
        await CreatePatAsync();
        _danaId = await CreateUserAsync("dana", "dana@example.test", enabled: false);

        _connectionString = await database.CreateDatabaseAsync("user_quota", CancellationToken);
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
            builder.UseSetting("Console:OperatorRole", "operator");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, SessionStartupFilter>();

                // A cell's quotas over the configured tenant catalog, with a users default of 2.
                services.AddSingleton(provider =>
                {
                    var cell = new CellOptions { Id = "vn-1" };
                    var settings = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Quotas:Defaults:users"] = "2" })
                        .Build();
                    return new TenantQuotas(cell, QuotaDefaults.Load(settings, cell), provider.GetRequiredService<ITenantCatalog>());
                });
            });
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

    // Mailpit is shared by the whole run, so every test invites addresses of its own.
    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private Task<HttpResponseMessage> InviteAsync(string cookie, string email) =>
        PostJsonAsync("/api/users/invite", cookie, new JsonObject { ["email"] = email, ["displayName"] = "Invited Person" }.ToJsonString());

    // The enabled users are pat and this one, which is the quota of 2.
    private Task<string> CreateSecondEnabledUserAsync() => CreateUserAsync("lee", "lee@example.test", enabled: true);

    private async Task<string> PatIdAsync() =>
        (await GetJsonAsync($"admin/realms/{_realm}/users?username=pat&exact=true")).EnumerateArray().Single().GetProperty("id").GetString()!;

    private async Task<bool> IsEnabledAsync(string id) => (await GetJsonAsync($"admin/realms/{_realm}/users/{id}")).GetProperty("enabled").GetBoolean();

    private async Task<long> EnabledAuditCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_connectionString!, "SELECT count(*) FROM axis.audit_records WHERE action = 'user.enabled'", CancellationToken))!;

    private async Task<long> InvitedAuditCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_connectionString!, "SELECT count(*) FROM axis.audit_records WHERE action = 'user.invited'", CancellationToken))!;

    // The axis client accepts the test host's callback and home, and puts the realm roles of a person in its roles claim.
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

    // An account that exists before any invitation, with a password and an authenticator, such as a development user.
    private async Task CreatePatAsync()
    {
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
    }

    private async Task<string> CreateUserAsync(string username, string email, bool enabled)
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users", new { username, enabled, email, firstName = username });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return created.Headers.Location!.Segments[^1];
    }

    private HttpClient CreateClient(bool cookies) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = cookies,
        });

    /// <summary>Signs a user in through the test-only path and returns the session cookie's value.</summary>
    private async Task<string> SessionCookieAsync(string user, string role)
    {
        using var client = CreateClient(cookies: false);
        using var response = await client.GetAsync(new Uri($"/test/session?user={user}&role={role}", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string cookie)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Axis-Csrf", "1");
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string path, string cookie, string body)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        return await client.SendAsync(request, CancellationToken);
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
    /// Answers <c>GET /test/session?user=&amp;role=</c> by signing the user in with that role and a login at the identity provider
    /// just now.
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
                        await context.SignInAsync(
                            Session.Scheme,
                            Session.CreatePrincipal(tenantId, user, user, [context.Request.Query["role"].ToString()], clock.GetUtcNow()));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
