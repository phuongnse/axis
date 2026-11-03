using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
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

namespace Axis.Integration.Tests;

/// <summary>
/// A preview tenant signs people in through its main tenant's realm and platform client <c>axis</c>, at a realm that
/// <see cref="TenantRealm"/> makes in a real Keycloak, with an account that needs a password and an authenticator code. Tenant
/// <c>main</c> is served at <c>main.example.test</c> and its preview tenant, <c>main-preview</c>, at <c>localhost</c>. Each has a database of its own.
/// A sign-in that starts on the page of a site that allows only a brokered provider still goes to the platform client, with no
/// provider hint and no <c>acr_values</c>, and the site then answers. The roles that the users page of <c>main</c> gives apply on
/// the preview at the next request, and a removed role does not. That holds for an application role and for a platform role.
/// </summary>
public sealed class PreviewTenantPlatformSignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Administrator = "administrator";
    private const string Employee = "employee";
    private const string MainHost = "main.example.test";
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
    private WebApplicationFactory<Program>? _factory;
    private TemporaryFolder? _application;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_person_signs_in_on_the_previews_site_page_through_the_main_tenants_realm_and_platform_client_without_a_hint()
    {
        using var app = CreateClient("localhost", cookies: true);

        using var challenge = await app.GetAsync("/api/auth/sign-in?returnUrl=/previewpartner", CancellationToken);

        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith(new Uri(_baseAddress, $"realms/{_realm}/protocol/openid-connect/auth?").AbsoluteUri, authorize.AbsoluteUri, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal("axis", query["client_id"]);
        Assert.Null(query["kc_idp_hint"]);
        Assert.Null(query["acr_values"]);

        await CompleteLoginAsync(app, authorize);

        using var me = await app.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(_patId, body.GetProperty("id").GetString());
        using var site = await app.GetAsync("/api/sites/previewpartner", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, site.StatusCode);
    }

    [Fact]
    public async Task A_role_that_the_main_tenants_users_page_gives_applies_on_the_preview_and_a_person_without_it_or_after_removal_is_denied()
    {
        var pat = await SessionCookieAsync("localhost", _patId, Employee, authenticatedMinutesAgo: null);
        var someone = await SessionCookieAsync("localhost", "someone", Employee, authenticatedMinutesAgo: null);
        var admin = await SessionCookieAsync(MainHost, Administrator, Administrator, authenticatedMinutesAgo: null);
        Assert.Equal(HttpStatusCode.Forbidden, await RecordsStatusAsync(pat));

        using var given = await PostJsonAsync(MainHost, $"/api/users/{_patId}/give-role", admin, Body("buyer"));

        Assert.Equal(HttpStatusCode.NoContent, given.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await RecordsStatusAsync(pat));
        Assert.Equal(HttpStatusCode.Forbidden, await RecordsStatusAsync(someone));

        using var removed = await PostJsonAsync(MainHost, $"/api/users/{_patId}/remove-role", admin, Body("buyer"));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await RecordsStatusAsync(pat));
    }

    [Fact]
    public async Task A_platform_role_that_the_main_tenants_users_page_gives_applies_on_the_preview_until_it_is_removed()
    {
        var pat = await SessionCookieAsync("localhost", _patId, Employee, authenticatedMinutesAgo: null);
        var admin = await SessionCookieAsync(MainHost, Administrator, Administrator, authenticatedMinutesAgo: 0);
        Assert.False(await ConsoleAsync(pat));

        using var given = await PostJsonAsync(MainHost, $"/api/users/{_patId}/give-role", admin, """{ "role": "operator" }""");

        Assert.Equal(HttpStatusCode.NoContent, given.StatusCode);
        Assert.True(await ConsoleAsync(pat));

        using var removed = await PostJsonAsync(MainHost, $"/api/users/{_patId}/remove-role", admin, """{ "role": "operator" }""");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(await ConsoleAsync(pat));
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, "activation-secret-1", UsersSecret), new Dictionary<string, string>(), CancellationToken);
        await ConfigureClientAsync();
        _patId = await CreatePatAsync();

        var databases = new Dictionary<string, string>();
        foreach (var tenant in new[] { "main", "preview" })
        {
            databases[tenant] = await database.CreateDatabaseAsync($"previewplatform_{tenant}", CancellationToken);
            await DataDatabaseFixture.MigrateTenantAsync(databases[tenant], CancellationToken);
        }

        _application = Application();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(databases["main"]);
            builder.UseSetting("Tenants:main:Hosts:0", MainHost);
            builder.UseSetting("Tenants:main:ConnectionString", PostgreSqlServer.RuntimeConnectionString(databases["main"]));
            builder.UseSetting("Tenants:main:SchemaConnectionString", databases["main"]);
            builder.UseSetting("Tenants:main:Oidc:Authority", new Uri(_baseAddress, $"realms/{_realm}").ToString());
            builder.UseSetting("Tenants:main:Oidc:ClientId", "axis");
            builder.UseSetting("Tenants:main:Oidc:ClientSecret", PlatformSecret);
            builder.UseSetting("Tenants:main:Oidc:UsersClientSecret", UsersSecret);
            builder.UseSetting("Tenants:main:Oidc:RolesClaim", "roles");
            builder.UseSetting("Tenants:main:Realm:Name", _realm);
            builder.UseSetting("Tenants:main:Realm:ActivationClientSecret", "activation-secret-1");
            builder.UseSetting("Tenants:main:Bindings:PartnerIssuer:Kind", "url");
            builder.UseSetting("Tenants:main:Bindings:PartnerIssuer:Value", new Uri(_baseAddress, "realms/master").AbsoluteUri);
            builder.UseSetting("Tenants:main:Bindings:PartnerClient:Kind", "secret");
            builder.UseSetting("Tenants:main:Bindings:PartnerClient:Value", "site-client-value");
            builder.UseSetting("Tenants:main:Bindings:PartnerSecret:Kind", "secret");
            builder.UseSetting("Tenants:main:Bindings:PartnerSecret:Value", "site-secret-value");
            builder.UseSetting("Tenants:main-preview:Hosts:0", "localhost");
            builder.UseSetting("Tenants:main-preview:ConnectionString", PostgreSqlServer.RuntimeConnectionString(databases["preview"]));
            builder.UseSetting("Tenants:main-preview:SchemaConnectionString", databases["preview"]);
            builder.UseSetting("Tenants:main-preview:PreviewOf", "main");
            builder.UseSetting("Users:AdministratorRole", Administrator);
            builder.UseSetting("Console:OperatorRole", "operator");
            builder.UseSetting("ActivateOnStartup:0", _application.Path);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _application?.Dispose();
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

    private HttpClient CreateClient(string host, bool cookies) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{host}"),
            AllowAutoRedirect = false,
            HandleCookies = cookies,
        });

    /// <summary>Posts pat's password and then a code at the realm's login form, and follows the callback on the server so that <paramref name="app"/> holds the session.</summary>
    private async Task CompleteLoginAsync(HttpClient app, Uri authorize)
    {
        using var browser = new RealmBrowser();
        using var page = await browser.GetAsync(authorize);
        var html = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        using var afterPassword = await browser.PostAsync(
            KeycloakSignIn.LoginAction(html).AbsoluteUri,
            new Dictionary<string, string> { ["username"] = "pat", ["password"] = _password, ["credentialId"] = "" });
        var codePage = await afterPassword.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(afterPassword.StatusCode == HttpStatusCode.OK && KeycloakSignIn.Page(codePage) == "twoStep", codePage);
        using var login = await browser.PostAsync(
            KeycloakSignIn.LoginAction(codePage).AbsoluteUri,
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
        Assert.Equal("/previewpartner", signedIn.Headers.Location?.OriginalString);
    }

    // The realm accepts only the code of the current 30 seconds, so the sign-in waits for a window with time left in it.
    private static async Task<string> NextAuthenticatorCodeAsync()
    {
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 >= 27)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        }

        return DevelopmentTotp.CodeFromSecret(AuthenticatorSecret, DateTimeOffset.UtcNow);
    }

    /// <summary>Signs a user in on <paramref name="host"/> through the test-only path, with a last login <paramref name="authenticatedMinutesAgo"/> minutes ago or none, and returns the session cookie's value.</summary>
    private async Task<string> SessionCookieAsync(string host, string user, string role, int? authenticatedMinutesAgo)
    {
        using var client = CreateClient(host, cookies: false);
        var query = authenticatedMinutesAgo is { } minutes ? $"&authenticatedMinutesAgo={minutes}" : string.Empty;
        using var response = await client.GetAsync(new Uri($"/test/session?user={user}&role={role}{query}", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string host, string path, string cookie, string body)
    {
        using var client = CreateClient(host, cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpStatusCode> RecordsStatusAsync(string cookie)
    {
        using var client = CreateClient("localhost", cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/apps/Roles/entities/Note/records", UriKind.Relative));
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    private async Task<bool> ConsoleAsync(string cookie)
    {
        using var client = CreateClient("localhost", cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("console").GetBoolean();
    }

    private static string Body(string role) => $$"""{ "applicationId": "{{_rolesId}}", "role": "{{role}}" }""";

    /// <summary>
    /// The application <c>Roles</c> with the role <c>buyer</c>, the entity <c>Note</c> and a policy that lets <c>buyer</c> read notes. It has the
    /// <c>accounts</c> provider, the <c>oidc</c> provider <c>partner</c> and the site <c>previewpartner</c>, which allows only <c>partner</c>.
    /// </summary>
    private static TemporaryFolder Application() => new TemporaryFolder()
        .With("application.json", $$"""{ "id": "{{_rolesId}}", "kind": "application", "name": "Roles", "formatVersion": 1 }""")
        .With("texts/en.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c10", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "role.buyer": "Buyer", "site.title": "Home", "nav.home": "Home", "pages.home.title": "Home", "signIn.partner": "Continue with Partner" } }
            """)
        .With("roles/buyer.json", """{ "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c13", "kind": "role", "name": "buyer", "formatVersion": 1, "label": { "textKey": "role.buyer" } }""")
        .With("entities/note.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c21", "kind": "entity", "name": "Note", "formatVersion": 1, "displayField": "title",
              "fields": [ { "name": "title", "type": "text", "required": true } ] }
            """)
        .With("policies/buyer-access.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c15", "kind": "policy", "name": "BuyerAccess", "formatVersion": 1,
              "roles": ["buyer"], "grants": [ { "name": "noteRead", "entity": "Note", "actions": ["read"], "fields": ["title"] } ] }
            """)
        .With("pages/home.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c31", "kind": "page", "name": "Home", "formatVersion": 1,
              "title": { "textKey": "pages.home.title" }, "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] }
            """)
        .With("identity-providers/accounts.json", """{ "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c32", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""")
        .With("identity-providers/partner.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c33", "kind": "identityProvider", "name": "partner", "formatVersion": 1, "type": "oidc",
              "label": { "textKey": "signIn.partner" },
              "issuer": { "binding": "PartnerIssuer" }, "clientId": { "binding": "PartnerClient" }, "clientSecret": { "binding": "PartnerSecret" } }
            """)
        .With("sites/previewpartner.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c34", "kind": "site", "name": "previewpartner", "formatVersion": 1, "path": "previewpartner", "signIn": ["partner"],
              "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
              "navigation": [ { "page": "Home", "label": { "textKey": "nav.home" } } ] }
            """);

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
