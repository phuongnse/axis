using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Configuration.Tests;
using static Axis.Integration.Tests.AdminActivateRealmTests;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant restore</c> on a real Keycloak. An archive whose realm is missing gets a new realm named after the new tenant id,
/// with the clients of the active releases and without the providers whose secret bindings have no value yet. An archive whose
/// realm exists keeps it, with its users, and no realm is created.
/// </summary>
public sealed class AdminTenantRestoreRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Issuer = "Issuer";
    private const string Client = "Client";
    private const string Secret = "Secret";

    private static readonly string[] _restoreSteps =
    [
        "check", "entry", "roles", "database", "restore", "grants", "grants", "work", "sessions", "role-check", "realm", "version",
    ];

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;
    private PostgresClientTools _tools = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Dictionary<string, string?> IdentityProvider => new()
    {
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _tools = await PostgresClientTools.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}", null);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task A_backup_restored_where_its_realm_is_missing_gets_a_new_realm_in_step_with_its_active_releases()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", original, Issuer, "--url", new Uri(_baseAddress, "realms/master").AbsoluteUri], extra: AdminScenario.Directory)).Exit);
        foreach (var name in new[] { Client, Secret })
        {
            Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", original, name, "--secret"], input: $"{name}-value-{original}", extra: AdminScenario.Directory)).Exit);
        }

        using var folder = Folder(new Application(), ["sales"], provider: true);
        Assert.Equal(0, (await scenario.RunAsync(["activate", folder.Path, "--tenant", original], identityProvider: _handler)).Exit);
        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(original));
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", original, "--out", file], path: _tools.PathSetting)).Exit);
        await DeleteRealmAsync(original);
        var restored = scenario.NewTenantId();
        _realms.Add(restored);

        var result = await RestoreAsync(scenario, file, restored, withClientSecret: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_restoreSteps.Select(step => $"{restored} {step} ok"), result.Output);
        Assert.Equal([$"{restored} warning the identity provider 'oidc-issuer' waits for the binding '{Client}', which has no value"], result.Error);
        Assert.Equal(["axis", "axis-activation", "axis-users", "site-sales"], await ClientIdsAsync(restored));
        Assert.Empty(await ProviderAliasesAsync(restored));
        var row = await scenario.RowAsync(restored);
        Assert.Equal(new Uri(_baseAddress, $"realms/{restored}").ToString(), row.OidcAuthority);
        Assert.Equal("axis", row.OidcClientId);
        Assert.Equal("suspended", row.Status);

        // The operator sets the secret bindings, which the archive never held. The next activation adds the provider.
        foreach (var name in new[] { Client, Secret })
        {
            Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", restored, name, "--secret"], input: $"{name}-value-{restored}", extra: AdminScenario.Directory)).Exit);
        }

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", restored])).Exit);
        var activated = await scenario.RunAsync(["activate", folder.Path, "--tenant", restored], identityProvider: _handler);
        Assert.Equal(0, activated.Exit);
        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(restored));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_backup_restored_while_its_realm_exists_keeps_the_realm_and_its_users_and_creates_no_realm()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        await CreateUserAsync(original, "kept-user");
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", original, "--out", file], path: _tools.PathSetting)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "suspend", original])).Exit);
        var restored = scenario.NewTenantId();
        _realms.Add(restored);

        var result = await RestoreAsync(scenario, file, restored, withClientSecret: true);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_restoreSteps.Where(step => step != "realm").Select(step => $"{restored} {step} ok"), result.Output);
        Assert.Empty(result.Error);
        Assert.False(await RealmExistsAsync(restored));
        Assert.Equal(["kept-user"], await UsernamesAsync(original, "kept-user"));
        var kept = await scenario.RowAsync(original);
        var row = await scenario.RowAsync(restored);
        Assert.Equal(kept.OidcAuthority, row.OidcAuthority);
        Assert.Equal("axis", row.OidcClientId);
        Assert.Null(row.RealmActivationClientSecret);
        await scenario.AssertNoSecretsAsync();
    }

    private Task<AdminResult> RestoreAsync(AdminScenario scenario, string file, string id, bool withClientSecret) =>
        scenario.RunAsync(
            AdminTenantRestoreTests.RestoreArgs(file, id, $"{id}.example.test"),
            withClientSecret: withClientSecret,
            path: _tools.PathSetting,
            extra: IdentityProvider,
            identityProvider: _handler);

    // Creates the tenant with its realm in the real Keycloak.
    private async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: IdentityProvider);
        Assert.Equal(0, created.Exit);
        return id;
    }

    private async Task DeleteRealmAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Delete, $"admin/realms/{realm}", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task CreateUserAsync(string realm, string username)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Post, $"admin/realms/{realm}/users", new { username, enabled = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<bool> RealmExistsAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}", null);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<string[]> ClientIdsAsync(string realm) =>
        [.. (await GetAsync(realm, "clients?max=1000")).EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString()!)
            .Where(clientId => clientId is "axis" or "axis-activation" or "axis-users" || clientId.StartsWith("site-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private async Task<string[]> ProviderAliasesAsync(string realm) =>
        [.. (await GetAsync(realm, "identity-provider/instances")).EnumerateArray()
            .Select(provider => provider.GetProperty("alias").GetString()!)
            .Where(alias => alias.StartsWith("oidc-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private async Task<string[]> UsernamesAsync(string realm, string username) =>
        [.. (await GetAsync(realm, $"users?username={username}&exact=true")).EnumerateArray().Select(user => user.GetProperty("username").GetString()!)];

    private async Task<JsonElement> GetAsync(string realm, string path)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}/{path}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<string> TokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body)
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
