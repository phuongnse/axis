using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant create</c> on a real Keycloak: it creates the tenant's realm with its three clients, stores the generated
/// client secrets encrypted and points the tenant's <c>Oidc</c> block at the realm. The realm uses the Axis login theme, and
/// a second run sets it again. A preview tenant gets no realm.
/// </summary>
public sealed class AdminTenantRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http, "master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task Create_makes_the_realm_with_three_clients_and_stores_the_secrets_encrypted_and_points_the_oidc_block_at_it()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);

        var result = await CreateAsync(scenario, id);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["entry", "realm", "roles", "database", "grants", "migrations", "grants", "role-check", "version"], result.Output.Select(line => line.Split(' ')[1]));
        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await TokenAsync(http, "master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var realm = await SendAsync(http, admin, HttpMethod.Get, $"admin/realms/{id}");
        Assert.Equal(HttpStatusCode.OK, realm.StatusCode);
        foreach (var clientId in new[] { "axis", "axis-activation", "axis-users" })
        {
            using var clients = await SendAsync(http, admin, HttpMethod.Get, $"admin/realms/{id}/clients?clientId={clientId}");
            var body = await clients.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal(1, body.GetArrayLength());
        }

        var row = await scenario.RowAsync(id);
        Assert.Equal($"{_baseAddress.GetLeftPart(UriPartial.Authority)}/realms/{id}", row.OidcAuthority);
        Assert.Equal("axis", row.OidcClientId);
        Assert.NotEmpty(row.OidcClientSecret);
        Assert.NotEmpty(row.RealmActivationClientSecret!);
        Assert.NotEmpty(row.RealmUsersClientSecret!);
        var activation = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.RealmActivationClientSecret, row.RealmActivationClientSecret!).Reveal();
        var users = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.RealmUsersClientSecret, row.RealmUsersClientSecret!).Reveal();
        Assert.NotEmpty(await TokenAsync(http, id, "axis-activation", activation));
        Assert.NotEmpty(await TokenAsync(http, id, "axis-users", users));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_gives_the_realm_the_axis_login_theme_and_its_sign_in_page_has_the_theme_context()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);

        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);

        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await TokenAsync(http, "master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        Assert.Equal(TenantRealm.LoginTheme, await LoginThemeAsync(http, admin, id));

        // The platform client has no redirect address yet, so the page is opened through the realm's account console.
        var redirect = Uri.EscapeDataString(new Uri(_baseAddress, $"realms/{id}/account/").ToString());
        var signIn = new Uri(
            _baseAddress,
            $"realms/{id}/protocol/openid-connect/auth?client_id=account-console&response_type=code&scope=openid&redirect_uri={redirect}"
            + $"&code_challenge={new string('a', 43)}&code_challenge_method=S256");
        using var page = await http.GetAsync(signIn, CancellationToken);
        var html = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        Assert.NotNull(KeycloakSignIn.LoginAction(html));
    }

    [Fact]
    public async Task A_rerun_of_create_sets_the_axis_login_theme_again()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);
        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);
        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await TokenAsync(http, "master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var changed = await SendAsync(http, admin, HttpMethod.Put, $"admin/realms/{id}", new Dictionary<string, object?> { ["loginTheme"] = "keycloak" });
        Assert.True(changed.IsSuccessStatusCode);
        Assert.Equal("keycloak", await LoginThemeAsync(http, admin, id));
        // A rerun only happens on an entry that is still being set up.
        await scenario.SetStatusAsync(id, "provisioning");

        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);

        Assert.Equal(TenantRealm.LoginTheme, await LoginThemeAsync(http, admin, id));
    }

    [Fact]
    public async Task A_preview_tenant_gets_no_realm_and_needs_no_identity_provider_setting()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var main = NewTenantId(scenario);
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await CreateAsync(scenario, main)).Exit);

        var result = await scenario.RunAsync(
            AdminScenario.PreviewArgs(preview, main, $"{preview}.example.test"),
            identityProvider: _handler,
            extra: new Dictionary<string, string?>
            {
                ["IdentityProvider:Url"] = null,
                ["IdentityProvider:AdminClientId"] = null,
                ["IdentityProvider:AdminClientSecret"] = null,
            });

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain(result.Output, line => line.Contains(" realm ", StringComparison.Ordinal));
        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await TokenAsync(http, "master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var realm = await SendAsync(http, admin, HttpMethod.Get, $"admin/realms/{preview}");
        Assert.Equal(HttpStatusCode.NotFound, realm.StatusCode);
    }

    [Fact]
    public async Task Sign_in_on_a_new_tenants_host_redirects_to_the_authorization_endpoint_of_its_realm()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);
        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);
        var authority = (await scenario.RowAsync(id)).OidcAuthority;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(scenario.Platform);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/sign-in", UriKind.Relative));
        request.Headers.Host = $"{id}.example.test";

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.AbsoluteUri;
        Assert.StartsWith($"{authority}/protocol/openid-connect/auth?", location, StringComparison.Ordinal);
        Assert.Contains("client_id=axis&", location + "&", StringComparison.Ordinal);
    }

    private string NewTenantId(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        return id;
    }

    // Runs against the real Keycloak, with its admin client and the real HTTP handler in place of the stub.
    private Task<AdminResult> CreateAsync(AdminScenario scenario, string id) =>
        scenario.RunAsync(
            AdminScenario.CreateArgs(id, $"{id}.example.test"),
            identityProvider: _handler,
            extra: new Dictionary<string, string?>
            {
                ["IdentityProvider:Url"] = _baseAddress.ToString(),
                ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
                ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
            });

    private async Task<string> TokenAsync(HttpClient http, string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<string?> LoginThemeAsync(HttpClient http, string token, string realm)
    {
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("loginTheme").GetString();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }
}
