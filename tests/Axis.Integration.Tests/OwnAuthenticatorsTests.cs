using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

namespace Axis.Integration.Tests;

/// <summary>
/// <c>GET /api/me/authenticators</c> answers a signed-in person with whether they are an account of the tenant's realm and with
/// their authenticator apps. The realm is one that <see cref="TenantRealm"/> makes in a real Keycloak, and a test-only path signs a
/// user in by the realm user's id, so nobody signs in for real. An account with two authenticators gets both with the id, the label
/// and the created date. A brokered user, a realm user with a federated identity, is not an account and gets an empty list. A
/// signed-out caller and a realm bearer token get 401, because the route takes a session only. The tenant is served at localhost.
/// </summary>
public sealed class OwnAuthenticatorsTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string PlatformSecret = "platform-secret-1";
    private const string UsersSecret = "users-secret-1";

    private static readonly string[] _labels = ["Phone", "Tablet"];

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task An_account_gets_both_of_its_authenticators_with_ids_and_labels()
    {
        var id = await CreateAccountAsync();
        var credentials = (await GetJsonAsync($"admin/realms/{_realm}/users/{id}/credentials")).EnumerateArray()
            .Where(credential => credential.GetProperty("type").GetString() == "otp")
            .ToList();
        Assert.Equal(2, credentials.Count);

        // Keycloak may drop a label that is given when the credential is created.
        foreach (var (credential, label) in credentials.Zip(_labels))
        {
            if (credential.TryGetProperty("userLabel", out var current) && current.GetString() == label)
            {
                continue;
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(_baseAddress, $"admin/realms/{_realm}/users/{id}/credentials/{credential.GetProperty("id").GetString()}/userLabel"))
            {
                Content = new StringContent(label, System.Text.Encoding.UTF8, "text/plain"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            using var labelled = await _http.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, labelled.StatusCode);
        }

        var realmIds = (await GetJsonAsync($"admin/realms/{_realm}/users/{id}/credentials")).EnumerateArray()
            .Where(credential => credential.GetProperty("type").GetString() == "otp")
            .Select(credential => credential.GetProperty("id").GetString()!)
            .ToList();
        var cookie = await SessionCookieAsync(id);

        using var response = await GetAsync("/api/me/authenticators", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.True(body.GetProperty("account").GetBoolean());
        var authenticators = body.GetProperty("authenticators").EnumerateArray().ToList();
        Assert.Equal(realmIds.Order(StringComparer.Ordinal), authenticators.Select(authenticator => authenticator.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(["Phone", "Tablet"], authenticators.Select(authenticator => authenticator.GetProperty("label").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(authenticators, authenticator => Assert.Equal(JsonValueKind.String, authenticator.GetProperty("createdAt").ValueKind));
    }

    [Fact]
    public async Task A_brokered_user_is_not_an_account_and_gets_no_authenticators()
    {
        using var provider = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/identity-provider/instances", new
        {
            alias = "company",
            providerId = "oidc",
            config = new Dictionary<string, string>
            {
                ["clientId"] = "x",
                ["clientSecret"] = "y",
                ["authorizationUrl"] = "https://idp.example.test/auth",
                ["tokenUrl"] = "https://idp.example.test/token",
            },
        });
        Assert.Equal(HttpStatusCode.Created, provider.StatusCode);
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users", new { username = "company.subject-1", enabled = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = created.Headers.Location!.Segments[^1];
        using var linked = await SendAsync(
            HttpMethod.Post, $"admin/realms/{_realm}/users/{id}/federated-identity/company", new { identityProvider = "company", userId = "subject-1", userName = "subject-1" });
        Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);
        var cookie = await SessionCookieAsync(id);

        using var response = await GetAsync("/api/me/authenticators", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.False(body.GetProperty("account").GetBoolean());
        Assert.Empty(body.GetProperty("authenticators").EnumerateArray());
    }

    [Fact]
    public async Task A_signed_out_caller_and_a_bearer_token_get_401()
    {
        using var signedOut = await GetAsync("/api/me/authenticators", cookie: null);
        using var problem = await RecordApiFixture.ReadProblemAsync(signedOut, HttpStatusCode.Unauthorized);
        Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "axis-users",
            ["client_secret"] = UsersSecret,
        });
        using var token = await _http.PostAsync(new Uri(_baseAddress, $"realms/{_realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        var bearer = (await token.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("access_token").GetString()!;

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me/authenticators", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var withBearer = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withBearer.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, "activation-secret-1", UsersSecret), new Dictionary<string, string>(), CancellationToken);

        var connectionString = await database.CreateDatabaseAsync("own_authenticators", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", connectionString);
            builder.UseSetting("Tenants:a:Oidc:Authority", new Uri(_baseAddress, $"realms/{_realm}").ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", "axis");
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", PlatformSecret);
            builder.UseSetting("Tenants:a:Oidc:UsersClientSecret", UsersSecret);
            builder.UseSetting("Tenants:a:Oidc:RolesClaim", "roles");
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

    // An account with a password and two authenticators of different secrets, each with a label.
    private async Task<string> CreateAccountAsync()
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
                new { type = "password", value = Guid.NewGuid().ToString("N"), temporary = false },
                Authenticator("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", "Phone"),
                Authenticator("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "Tablet"),
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return created.Headers.Location!.Segments[^1];
    }

    private static object Authenticator(string secret, string label) => new
    {
        type = "otp",
        userLabel = label,
        secretData = $$"""{"value":"{{secret}}"}""",
        credentialData = """{"subType":"totp","digits":6,"period":30,"algorithm":"HmacSHA1","counter":0,"secretEncoding":"BASE32"}""",
    };

    private HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Signs the realm user in through the test-only path and returns the session cookie's value.</summary>
    private async Task<string> SessionCookieAsync(string userId)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(new Uri($"/test/session?user={userId}&role=employee", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? cookie)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

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

    /// <summary>Answers <c>GET /test/session?user=&amp;role=</c> by signing the user in with that role and a login just now.</summary>
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
