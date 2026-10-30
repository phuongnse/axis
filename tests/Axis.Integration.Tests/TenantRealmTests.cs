using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Admin;

namespace Axis.Integration.Tests;

/// <summary>
/// <see cref="TenantRealm"/> creates a tenant's realm in a real Keycloak with its display name, sessions of at most
/// 8 hours and three clients. The activation client has rights over providers and clients but not users, and the users
/// client has rights over users only. A second run brings the realm in step and leaves one of each client.
/// </summary>
public sealed class TenantRealmTests(KeycloakFixture keycloak) : IAsyncLifetime
{
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
    public async Task Creates_the_realm_with_its_display_name_session_limit_and_three_clients()
    {
        var realm = NewRealm();
        await EnsureAsync(realm, "Acme Corporation", _secrets);

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var body = await GetJsonAsync(token, $"admin/realms/{realm}");
        Assert.Equal("Acme Corporation", body.GetProperty("displayName").GetString());
        var lifespan = body.GetProperty("ssoSessionMaxLifespan").GetInt32();
        Assert.InRange(lifespan, 1, TenantRealm.SessionMaxLifespanSeconds);
        var rememberMe = body.GetProperty("ssoSessionMaxLifespanRememberMe").GetInt32();
        Assert.InRange(rememberMe, 1, TenantRealm.SessionMaxLifespanSeconds);

        foreach (var clientId in new[] { "axis", "axis-activation", "axis-users" })
        {
            Assert.Equal(1, await CountClientsAsync(token, realm, clientId));
        }
    }

    [Fact]
    public async Task The_activation_client_manages_clients_and_not_users()
    {
        var realm = NewRealm();
        await EnsureAsync(realm, "Acme Corporation", _secrets);
        var token = await TokenAsync(realm, "axis-activation", _secrets.Activation);

        using var users = await SendAsync(token, HttpMethod.Get, $"admin/realms/{realm}/users");
        using var clients = await SendAsync(token, HttpMethod.Get, $"admin/realms/{realm}/clients");

        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clients.StatusCode);
    }

    [Fact]
    public async Task The_users_client_manages_users_and_not_providers_or_clients()
    {
        var realm = NewRealm();
        await EnsureAsync(realm, "Acme Corporation", _secrets);
        var token = await TokenAsync(realm, "axis-users", _secrets.Users);

        var provider = new
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
        };
        using var createProvider = await SendAsync(token, HttpMethod.Post, $"admin/realms/{realm}/identity-provider/instances", provider);
        using var createClient = await SendAsync(token, HttpMethod.Post, $"admin/realms/{realm}/clients", new { clientId = "other" });
        using var users = await SendAsync(token, HttpMethod.Get, $"admin/realms/{realm}/users");

        Assert.Equal(HttpStatusCode.Forbidden, createProvider.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createClient.StatusCode);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
    }

    [Fact]
    public async Task A_second_run_updates_the_realm_and_leaves_one_of_each_client()
    {
        var realm = NewRealm();
        await EnsureAsync(realm, "Acme Corporation", _secrets);
        var renewed = new TenantRealmSecrets("platform-secret-2", "activation-secret-2", "users-secret-2");
        await EnsureAsync(realm, "Acme Holdings", renewed);

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var body = await GetJsonAsync(token, $"admin/realms/{realm}");
        Assert.Equal("Acme Holdings", body.GetProperty("displayName").GetString());
        foreach (var clientId in new[] { "axis", "axis-activation", "axis-users" })
        {
            Assert.Equal(1, await CountClientsAsync(token, realm, clientId));
        }

        Assert.NotEmpty(await TokenAsync(realm, "axis-activation", renewed.Activation));
        Assert.NotEmpty(await TokenAsync(realm, "axis-users", renewed.Users));
        using var stale = await RequestTokenAsync(realm, "axis-activation", _secrets.Activation);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    private string NewRealm()
    {
        var realm = $"t{Guid.NewGuid():N}"[..16];
        _realms.Add(realm);
        return realm;
    }

    private Task EnsureAsync(string realm, string displayName, TenantRealmSecrets secrets) =>
        new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(realm, displayName, secrets, new Dictionary<string, string>(), TestContext.Current.CancellationToken);

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
        using var response = await RequestTokenAsync(realm, clientId, secret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        return await _http.PostAsync(new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, TestContext.Current.CancellationToken);
    }
}
