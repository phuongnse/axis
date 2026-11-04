using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant legal set</c> and <c>clear</c> write the tenant's realm in a real Keycloak: the legal links on every client, the
/// Terms and Conditions step while the tenant has terms, and, when the terms version changes, the step on every existing user. A person who
/// signs in after the terms changed must accept them again, so a user can never claim that the terms were not shown (the repudiation
/// threat of the terms in the product threat model, P112).
/// </summary>
public sealed class AdminTenantLegalRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string TermsAction = "TERMS_AND_CONDITIONS";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task Set_writes_the_legal_links_on_every_client_and_turns_the_terms_step_on_for_every_user()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        Assert.False((await TermsActionAsync(id)).Enabled);
        await CreateClientAsync(id, "site-sales");
        var first = await CreateUserAsync(id, "first");
        var second = await CreateUserAsync(id, "second");

        var tenant = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy", "--terms-url", "https://tenant.test/terms", "--terms-version", "1"],
            identityProvider: _handler,
            extra: Settings());
        var site = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--site", "sales", "--privacy-url", "https://sales.test/privacy"], identityProvider: _handler, extra: Settings());

        Assert.Equal(0, tenant.Exit);
        Assert.Equal([$"{id} legal ok"], tenant.Output);
        Assert.Equal(0, site.Exit);
        Assert.Equal([$"{id} legal ok"], site.Output);
        var platform = await ClientAttributesAsync(id, "axis");
        Assert.Equal("https://tenant.test/privacy", platform["axis.privacyUrl"]);
        Assert.Equal("https://tenant.test/terms", platform["axis.termsUrl"]);
        Assert.Equal($"https://{id}.example.test/licences", platform["axis.licencesUrl"]);
        Assert.Equal("https://tenant.test/privacy", platform["axis.tenantPrivacyUrl"]);
        Assert.Equal("https://tenant.test/terms", platform["axis.tenantTermsUrl"]);
        Assert.True(platform.ContainsKey("post.logout.redirect.uris"), "The sync keeps the other attributes of the client.");

        // A site that sets its own link uses only its own pair, and the tenant's pair stays on the client for the terms step.
        var sales = await ClientAttributesAsync(id, "site-sales");
        Assert.Equal("https://sales.test/privacy", sales["axis.privacyUrl"]);
        Assert.False(sales.ContainsKey("axis.termsUrl"));
        Assert.Equal($"https://{id}.example.test/licences", sales["axis.licencesUrl"]);
        Assert.Equal("https://tenant.test/privacy", sales["axis.tenantPrivacyUrl"]);
        Assert.Equal("https://tenant.test/terms", sales["axis.tenantTermsUrl"]);

        var action = await TermsActionAsync(id);
        Assert.True(action.Enabled);
        Assert.True(action.DefaultAction);
        Assert.Contains(TermsAction, await RequiredActionsAsync(id, first));
        Assert.Contains(TermsAction, await RequiredActionsAsync(id, second));
        Assert.Equal("1", await StoredTermsVersionAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_new_terms_version_asks_every_user_again_and_a_change_of_the_addresses_alone_asks_no_one_and_clear_turns_the_step_off()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var first = await CreateUserAsync(id, "first");
        var second = await CreateUserAsync(id, "second");
        var set = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy", "--terms-url", "https://tenant.test/terms", "--terms-version", "1"],
            identityProvider: _handler,
            extra: Settings());
        Assert.Equal(0, set.Exit);
        await RemoveRequiredActionsAsync(id, first);
        await RemoveRequiredActionsAsync(id, second);

        var addresses = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy-2", "--terms-url", "https://tenant.test/terms-2", "--terms-version", "1"],
            identityProvider: _handler,
            extra: Settings());

        Assert.Equal(0, addresses.Exit);
        Assert.Equal("https://tenant.test/terms-2", (await ClientAttributesAsync(id, "axis"))["axis.termsUrl"]);
        Assert.Empty(await RequiredActionsAsync(id, first));
        Assert.Empty(await RequiredActionsAsync(id, second));

        var version = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy-2", "--terms-url", "https://tenant.test/terms-2", "--terms-version", "2"],
            identityProvider: _handler,
            extra: Settings());

        Assert.Equal(0, version.Exit);
        Assert.Contains(TermsAction, await RequiredActionsAsync(id, first));
        Assert.Contains(TermsAction, await RequiredActionsAsync(id, second));

        var cleared = await scenario.RunAsync(["tenant", "legal", "clear", id], identityProvider: _handler, extra: Settings());

        Assert.Equal(0, cleared.Exit);
        Assert.Equal([$"{id} legal ok"], cleared.Output);
        var action = await TermsActionAsync(id);
        Assert.False(action.Enabled);
        Assert.False(action.DefaultAction);
        var attributes = await ClientAttributesAsync(id, "axis");
        foreach (var name in new[] { "axis.privacyUrl", "axis.termsUrl", "axis.tenantPrivacyUrl", "axis.tenantTermsUrl" })
        {
            Assert.False(attributes.ContainsKey(name), name);
        }

        // The licences page does not depend on the tenant's own links, so its address stays.
        Assert.Equal($"https://{id}.example.test/licences", attributes["axis.licencesUrl"]);
        Assert.Null(await StoredTermsVersionAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_failed_realm_write_keeps_the_stored_links_and_exits_with_one()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);

        var result = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy"], identityProvider: new RefusingHandler(), extra: Settings());

        Assert.Equal(1, result.Exit);
        Assert.StartsWith($"{id} realm failed:", Assert.Single(result.Output), StringComparison.Ordinal);
        Assert.Equal("https://tenant.test/privacy", (await scenario.RowAsync(id)).PrivacyUrl);
    }

    // The settings that the commands need: the directory source and the real identity provider.
    private Dictionary<string, string?> Settings() => new()
    {
        ["Tenancy:Source"] = "directory",
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    // Creates the tenant with its realm in the real Keycloak.
    private async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: Settings());
        Assert.Equal(0, created.Exit);
        return id;
    }

    private async Task CreateClientAsync(string realm, string clientId)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Post, $"admin/realms/{realm}/clients", new { clientId, publicClient = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string> CreateUserAsync(string realm, string name)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(
            http, token, HttpMethod.Post, $"admin/realms/{realm}/users", new { username = name, email = $"{name}@example.test", enabled = true, firstName = "Pat", lastName = "Example" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        return location[(location.LastIndexOf('/') + 1)..];
    }

    private async Task<JsonObject> ReadUserAsync(HttpClient http, string token, string realm, string userId)
    {
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(CancellationToken))!;
    }

    private async Task<string[]> RequiredActionsAsync(string realm, string userId)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var user = await ReadUserAsync(http, await MasterTokenAsync(http), realm, userId);
        return [.. (user["requiredActions"]?.AsArray() ?? []).Select(action => action!.GetValue<string>())];
    }

    private async Task RemoveRequiredActionsAsync(string realm, string userId)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        var user = await ReadUserAsync(http, token, realm, userId);
        user["requiredActions"] = new JsonArray();
        using var response = await SendAsync(http, token, HttpMethod.Put, $"admin/realms/{realm}/users/{userId}", user);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<Dictionary<string, string>> ClientAttributesAsync(string realm, string clientId)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}/clients?clientId={clientId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var client = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).EnumerateArray());
        return client.GetProperty("attributes").EnumerateObject().ToDictionary(attribute => attribute.Name, attribute => attribute.Value.GetString()!);
    }

    private async Task<(bool Enabled, bool DefaultAction)> TermsActionAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}/authentication/required-actions/{TermsAction}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, false);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var action = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return (action.GetProperty("enabled").GetBoolean(), action.GetProperty("defaultAction").GetBoolean());
    }

    private async Task<string?> StoredTermsVersionAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.TryGetProperty("attributes", out var attributes) && attributes.TryGetProperty("axis.termsVersion", out var version) && version.GetString() is { Length: > 0 } text
            ? text
            : null;
    }

    private async Task<string> MasterTokenAsync(HttpClient http)
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

    // An identity provider that answers every request with a server error.
    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
