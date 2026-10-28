using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Admin;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// <see cref="RealmSync"/> makes the site clients and brokered <c>oidc</c> providers of a realm that
/// <see cref="TenantRealm"/> made match a desired state. It changes only clients that start with <c>site-</c> and
/// providers that start with <c>oidc-</c>, puts the provider's alias into each site client's ID token, and changes
/// nothing when it cannot sign in as <c>axis-activation</c>.
/// </summary>
public sealed class RealmSyncTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string Alias = "oidc-companyissuer";

    private static readonly TenantRealmSecrets _secrets = new("platform-secret-1", "activation-secret-1", "users-secret-1");

    private readonly HttpClient _http = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _http.Dispose();
    }

    [Fact]
    public async Task Two_sites_and_one_provider_give_two_clients_and_a_provider_with_its_mappers()
    {
        var realm = await NewRealmAsync();
        await SyncAsync(realm, _secrets.Activation, State(["purchasing", "service-desk"], [Provider()]));

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        Assert.Equal(1, await CountClientsAsync(token, realm, "site-purchasing"));
        Assert.Equal(1, await CountClientsAsync(token, realm, "site-service-desk"));
        var provider = await GetJsonAsync(token, $"admin/realms/{realm}/identity-provider/instances/{Alias}");
        Assert.Equal("oidc", provider.GetProperty("providerId").GetString());
        Assert.Equal(["display name", "email", "locale", "roles groups"], await MapperNamesAsync(token, realm));
    }

    [Fact]
    public async Task A_second_sync_removes_the_other_site_and_the_provider_and_keeps_every_other_client()
    {
        var realm = await NewRealmAsync();
        await SyncAsync(realm, _secrets.Activation, State(["purchasing", "service-desk"], [Provider()]));
        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var handMade = await SendAsync(token, HttpMethod.Post, $"admin/realms/{realm}/clients", new { clientId = "operator-app" });
        Assert.Equal(HttpStatusCode.Created, handMade.StatusCode);

        await SyncAsync(realm, _secrets.Activation, State(["purchasing"], []));

        Assert.Equal(1, await CountClientsAsync(token, realm, "site-purchasing"));
        Assert.Equal(0, await CountClientsAsync(token, realm, "site-service-desk"));
        using var provider = await SendAsync(token, HttpMethod.Get, $"admin/realms/{realm}/identity-provider/instances/{Alias}");
        Assert.Equal(HttpStatusCode.NotFound, provider.StatusCode);
        foreach (var clientId in new[] { "axis", "axis-activation", "axis-users", "operator-app" })
        {
            Assert.Equal(1, await CountClientsAsync(token, realm, clientId));
        }
    }

    [Fact]
    public async Task A_site_client_puts_the_provider_alias_into_the_id_token()
    {
        var realm = await NewRealmAsync();
        await SyncAsync(realm, _secrets.Activation, State(["purchasing"], [Provider()]));

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var clients = await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing");
        var mappers = clients[0].GetProperty("protocolMappers").EnumerateArray().ToList();
        var mapper = Assert.Single(mappers, m => m.GetProperty("protocolMapper").GetString() == "oidc-usersessionmodel-note-mapper");
        var config = mapper.GetProperty("config");
        Assert.Equal("identity_provider", config.GetProperty("user.session.note").GetString());
        Assert.Equal("identity_provider", config.GetProperty("claim.name").GetString());
        Assert.Equal("true", config.GetProperty("id.token.claim").GetString());
    }

    [Fact]
    public async Task A_wrong_activation_secret_throws_and_changes_nothing()
    {
        var realm = await NewRealmAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SyncAsync(realm, "not-the-secret", State(["purchasing"], [Provider()])));

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        Assert.Equal(0, await CountClientsAsync(token, realm, "site-purchasing"));
        using var provider = await SendAsync(token, HttpMethod.Get, $"admin/realms/{realm}/identity-provider/instances/{Alias}");
        Assert.Equal(HttpStatusCode.NotFound, provider.StatusCode);
    }

    [Fact]
    public async Task Syncing_the_same_state_twice_leaves_one_client_per_site_and_one_provider_with_four_mappers()
    {
        var realm = await NewRealmAsync();
        var state = State(["purchasing", "service-desk"], [Provider()]);
        await SyncAsync(realm, _secrets.Activation, state);
        await SyncAsync(realm, _secrets.Activation, state);

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        Assert.Equal(1, await CountClientsAsync(token, realm, "site-purchasing"));
        Assert.Equal(1, await CountClientsAsync(token, realm, "site-service-desk"));
        var providers = await GetJsonAsync(token, $"admin/realms/{realm}/identity-provider/instances");
        Assert.Equal(1, providers.EnumerateArray().Count(p => p.GetProperty("alias").GetString() == Alias));
        Assert.Equal(4, (await MapperNamesAsync(token, realm)).Count);
        var clients = await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing");
        Assert.Equal(1, clients[0].GetProperty("protocolMappers").EnumerateArray()
            .Count(m => m.GetProperty("protocolMapper").GetString() == "oidc-usersessionmodel-note-mapper"));
    }

    [Fact]
    public async Task A_provider_alias_outside_the_scheme_and_a_repeated_site_path_throw_before_any_call()
    {
        var realm = await NewRealmAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => SyncAsync(realm, _secrets.Activation, State([], [Provider() with { Alias = "company" }])));
        await Assert.ThrowsAsync<ArgumentException>(
            () => SyncAsync(realm, _secrets.Activation, State(["purchasing", "purchasing"], [])));
    }

    private RealmBrokeredProvider Provider() =>
        new(Alias, new Uri(_baseAddress, "realms/master"), "company-client", "company-secret", "name", "email", "locale", ["groups"]);

    private static RealmState State(string[] sitePaths, RealmBrokeredProvider[] providers) => new(sitePaths, providers);

    private async Task<string> NewRealmAsync()
    {
        var realm = $"t{Guid.NewGuid():N}"[..16];
        _realms.Add(realm);
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(realm, "Acme", _secrets, TestContext.Current.CancellationToken);
        return realm;
    }

    private Task SyncAsync(string realm, string activationSecret, RealmState state) =>
        new RealmSync(_http, _baseAddress).SyncAsync(realm, activationSecret, state, TestContext.Current.CancellationToken);

    private async Task<List<string>> MapperNamesAsync(string token, string realm)
    {
        var mappers = await GetJsonAsync(token, $"admin/realms/{realm}/identity-provider/instances/{Alias}/mappers");
        return mappers.EnumerateArray().Select(m => m.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToList();
    }

    private async Task<int> CountClientsAsync(string token, string realm, string clientId)
    {
        var clients = await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId={clientId}");
        return clients.GetArrayLength();
    }

    private async Task<JsonElement> GetJsonAsync(string token, string path)
    {
        using var response = await SendAsync(token, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> TokenAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await _http.PostAsync(
            new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }
}
