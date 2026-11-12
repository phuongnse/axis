using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// The setup of the tests that follow an authoring request through a real Keycloak, see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>.
/// <see cref="TenantRealm"/> makes the realm, the sync adds the MCP audience, and the author's realm user <c>pat</c> holds the realm role <c>author</c>, which the
/// clients <c>axis</c> and <c>axis-agent</c> put into the <c>roles</c> claim. The server runs on a schema database as tenant <see cref="Tenant"/>, and the agent
/// host of a test uses <see cref="AgentSettings"/> to serve the same tenant. Call <see cref="InitializeAsync"/> before use. A test may name the author's
/// first and last name, and add server settings with <c>configure</c>, which gets the host builder and the path of the studio folder.
/// </summary>
internal sealed class AuthoringRealm(
    KeycloakFixture keycloak,
    PostgreSqlFixture database,
    string firstName = "Pat",
    string lastName = "Example",
    Action<IWebHostBuilder, string>? configure = null) : IAsyncDisposable
{
    public const string Tenant = "a";
    public const string Host = "localhost";
    public const string Url = "/api/studio/apps/PurchaseRequests/authoring-requests";
    public const string Text = """{ "text": "Add a cost centre to every purchase request." }""";

    private const string AuthenticatorSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string PlatformSecret = "platform-secret-1";
    private const string AgentSecret = "agent-secret-1";
    private const string ActivationSecret = "activation-secret-1";
    private const string AuthorRole = "author";

    // The realm's client axis accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;
    private string? _userId;
    private string? _schemaConnection;
    private TemporaryFolder? _folder;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>The server, which serves <see cref="Host"/> as the tenant and trusts the realm.</summary>
    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    /// <summary>The connection string of the tenant's database as its owner.</summary>
    public string SchemaConnection => _schemaConnection ?? throw new InvalidOperationException("The test host is not started.");

    public Uri Authority => new(_baseAddress, $"realms/{_realm}");

    public string UserId => _userId ?? throw new InvalidOperationException("The author is not made.");

    public string DataKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The settings under which an agent host serves the tenant of the server, as <see cref="AgentTestHost.TenantSettings"/> says.</summary>
    public Dictionary<string, string?> AgentSettings() =>
        AgentTestHost.TenantSettings(Tenant, Host, PostgreSqlServer.RuntimeConnectionString(SchemaConnection), Authority, PlatformSecret, AgentSecret, DataKey);

    public async Task InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, ActivationSecret, "users-secret-1", AgentSecret), new Dictionary<string, string>(), CancellationToken);

        // The sync makes the scope and the MCP audience, and replaces the redirect addresses of axis, so the test host's callback comes after it.
        await new RealmSync(_http, _baseAddress).SyncPlatformAddressesAsync(_realm, ActivationSecret, [Host], CancellationToken);
        await ConfigureRolesAsync();
        await CreateUserAsync();

        _folder = StudioPublishTests.NewFolder();
        _schemaConnection = await database.CreateDatabaseAsync("authoringexchange", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_schemaConnection, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_schemaConnection);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_schemaConnection));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", _schemaConnection);
            builder.UseSetting($"Tenants:{Tenant}:Oidc:Authority", Authority.ToString());
            builder.UseSetting($"Tenants:{Tenant}:Oidc:ClientId", TenantRealm.PlatformClientId);
            builder.UseSetting($"Tenants:{Tenant}:Oidc:ClientSecret", PlatformSecret);
            builder.UseSetting($"Tenants:{Tenant}:Oidc:AgentClientSecret", AgentSecret);
            builder.UseSetting($"Tenants:{Tenant}:Oidc:RolesClaim", "roles");
            builder.UseSetting($"Tenants:{Tenant}:DataKeys:1", DataKey);
            builder.UseSetting("Studio:Folders:0", _folder.Path);
            builder.UseSetting("Studio:AuthorRole", AuthorRole);
            configure?.Invoke(builder, _folder.Path);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _folder?.Dispose();

        // A test that runs the checks can outlast the master realm's 60-second token, so the delete takes a fresh one.
        _adminToken = await MasterTokenAsync();
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{_realm}");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    /// <summary>The tenant's data keys, which decrypt the token a send stores.</summary>
    public SensitiveValues Values() =>
        new(new TenantKeys(new ConfigurationTenantKeySource(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"Tenants:{Tenant}:DataKeys:1"] = DataKey })
            .Build())));

    /// <summary>Signs in through the platform client at the realm's login form, and checks that the session holds the author role.</summary>
    public async Task<HttpClient> SignInAsync()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var callback = await KeycloakSignIn.LogInAsync(
            client, Authority, "pat", _serverAddress, returnUrl: "/", code: await NextAuthenticatorCodeAsync(), password: _password);
        using var signedIn = await client.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        using var me = await client.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains($"\"{AuthorRole}\"", await me.Content.ReadAsStringAsync(CancellationToken));
        return client;
    }

    /// <summary>The access token that the author's stored refresh token yields once it is refreshed as <c>axis-agent</c>.</summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(string refreshToken) =>
        await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = RealmAgentClient.ClientId,
            ["client_secret"] = AgentSecret,
            ["refresh_token"] = refreshToken,
        });

    /// <summary>Takes the realm role <c>author</c> from the author's realm user.</summary>
    public async Task RemoveAuthorRoleAsync()
    {
        using var removed = await SendAsync(HttpMethod.Delete, $"admin/realms/{_realm}/users/{UserId}/role-mappings/realm", new[] { await RoleAsync() });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    /// <summary>Logs the author out at the realm, as an admin does.</summary>
    public async Task LogOutAuthorAsync()
    {
        using var loggedOut = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users/{UserId}/logout");
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
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

    // The realm role goes into the roles claim of both clients. The agent client's full scope is off, so each client gets a scope mapping for the role.
    private async Task ConfigureRolesAsync()
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/roles", new { name = AuthorRole });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await RoleAsync();

        foreach (var clientId in new[] { TenantRealm.PlatformClientId, RealmAgentClient.ClientId })
        {
            var client = (await GetJsonAsync($"admin/realms/{_realm}/clients?clientId={clientId}")).EnumerateArray().Single();
            var uuid = client.GetProperty("id").GetString()!;
            if (clientId == TenantRealm.PlatformClientId)
            {
                // The sync replaced the redirect addresses, so the test host's callback goes back in.
                var representation = JsonNode.Parse(client.GetRawText())!.AsObject();
                representation["redirectUris"] = new JsonArray("http://localhost:5206/*");
                using var updated = await SendAsync(HttpMethod.Put, $"admin/realms/{_realm}/clients/{uuid}", representation);
                Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            }

            using var mapped = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/clients/{uuid}/protocol-mappers/models", new
            {
                name = "realm roles",
                protocol = "openid-connect",
                protocolMapper = "oidc-usermodel-realm-role-mapper",
                config = new Dictionary<string, string>
                {
                    ["claim.name"] = "roles",
                    ["multivalued"] = "true",
                    ["jsonType.label"] = "String",
                    ["access.token.claim"] = "true",
                    ["id.token.claim"] = "true",
                    ["userinfo.token.claim"] = "false",
                },
            });
            Assert.Equal(HttpStatusCode.Created, mapped.StatusCode);
            using var scoped = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/clients/{uuid}/scope-mappings/realm", new[] { role });
            Assert.Equal(HttpStatusCode.NoContent, scoped.StatusCode);
        }
    }

    private async Task<JsonElement> RoleAsync() => await GetJsonAsync($"admin/realms/{_realm}/roles/{AuthorRole}");

    // The account signs in with a password and a code from an authenticator, and holds the author role.
    private async Task CreateUserAsync()
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users", new
        {
            username = "pat",
            enabled = true,
            emailVerified = true,
            email = "pat@example.test",
            firstName,
            lastName,
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
        _userId = (await GetJsonAsync($"admin/realms/{_realm}/users?username=pat&exact=true")).EnumerateArray().Single().GetProperty("id").GetString()!;
        using var granted = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users/{_userId}/role-mappings/realm", new[] { await RoleAsync() });
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> TokenAsync(Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(new Uri(_baseAddress, $"realms/{_realm}/protocol/openid-connect/token"), content, CancellationToken);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken));
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
}
