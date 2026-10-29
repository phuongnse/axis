using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Admin;
using Axis.Configuration.Realms;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// <see cref="RealmSync"/> makes the site clients and brokered <c>oidc</c> providers of a realm that
/// <see cref="TenantRealm"/> made match a desired state. It changes only clients that start with <c>site-</c> and
/// providers that start with <c>oidc-</c>, puts the provider's alias into each site client's ID token, points each
/// provider at the realm's first-login flow and gives it a username mapper, and changes
/// nothing when it cannot sign in as <c>axis-activation</c>. A site client also holds a copy of each protocol mapper of
/// <c>axis</c>, and a second sync leaves one copy of each mapper.
/// </summary>
public sealed partial class RealmSyncTests(KeycloakFixture keycloak) : IAsyncLifetime
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
        Assert.Equal(["display name", "email", "locale", "roles groups", "username"], await MapperNamesAsync(token, realm));
        Assert.Equal(RealmSync.FirstLoginFlowAlias, provider.GetProperty("firstBrokerLoginFlowAlias").GetString());
        var mappers = await GetJsonAsync(token, $"admin/realms/{realm}/identity-provider/instances/{Alias}/mappers");
        var username = Assert.Single(mappers.EnumerateArray(), m => m.GetProperty("name").GetString() == "username");
        Assert.Equal("oidc-username-idp-mapper", username.GetProperty("identityProviderMapper").GetString());
        Assert.Equal("${ALIAS}.${CLAIM.sub}", username.GetProperty("config").GetProperty("template").GetString());
        Assert.Equal("IMPORT", username.GetProperty("config").GetProperty("syncMode").GetString());
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
    public async Task A_site_client_copies_the_protocol_mappers_of_the_platform_client_once()
    {
        var realm = await NewRealmAsync();
        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var platform = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=axis"))[0];
        var rolesConfig = new Dictionary<string, string>
        {
            ["usermodel.clientRoleMapping.clientId"] = "axis",
            ["claim.name"] = "roles",
            ["multivalued"] = "true",
            ["jsonType.label"] = "String",
            ["id.token.claim"] = "true",
            ["access.token.claim"] = "true",
            ["userinfo.token.claim"] = "true",
        };
        using (var created = await SendAsync(token, HttpMethod.Post, $"admin/realms/{realm}/clients/{platform.GetProperty("id").GetString()}/protocol-mappers/models", new
        {
            name = "roles",
            protocol = "openid-connect",
            protocolMapper = "oidc-usermodel-client-role-mapper",
            config = rolesConfig,
        }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var state = State(["purchasing"], []);
        await SyncAsync(realm, _secrets.Activation, state);
        await SyncAsync(realm, _secrets.Activation, state);

        var site = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing"))[0];
        var mappers = site.GetProperty("protocolMappers").EnumerateArray().ToList();
        var roles = Assert.Single(mappers, mapper => mapper.GetProperty("name").GetString() == "roles");
        Assert.Equal("oidc-usermodel-client-role-mapper", roles.GetProperty("protocolMapper").GetString());
        Assert.Equal(
            rolesConfig,
            roles.GetProperty("config").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!));
        Assert.Single(mappers, mapper => mapper.GetProperty("name").GetString() == "identity provider");
    }

    [Fact]
    public async Task A_site_client_copies_the_addresses_of_the_platform_client_and_takes_the_derived_secret()
    {
        var realm = await NewRealmAsync();
        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var platform = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=axis"))[0];
        var platformBody = JsonNode.Parse(platform.GetRawText())!.AsObject();
        platformBody["redirectUris"] = new JsonArray("https://acme.test/signin-oidc");
        platformBody["webOrigins"] = new JsonArray("+");
        var attributes = platformBody["attributes"] as JsonObject ?? [];
        attributes["post.logout.redirect.uris"] = "https://acme.test/##https://acme.test/x";
        platformBody["attributes"] = attributes;
        using (var put = await SendAsync(token, HttpMethod.Put, $"admin/realms/{realm}/clients/{platform.GetProperty("id").GetString()}", platformBody))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var state = State(["purchasing"], []) with { PlatformClientSecret = _secrets.Platform };
        await SyncAsync(realm, _secrets.Activation, state);

        var site = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing"))[0];
        Assert.Equal(["https://acme.test/signin-oidc"], site.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString()));
        Assert.Equal(["+"], site.GetProperty("webOrigins").EnumerateArray().Select(u => u.GetString()));
        Assert.Equal("https://acme.test/##https://acme.test/x", site.GetProperty("attributes").GetProperty("post.logout.redirect.uris").GetString());
        var uuid = site.GetProperty("id").GetString();
        var secret = (await GetJsonAsync(token, $"admin/realms/{realm}/clients/{uuid}/client-secret")).GetProperty("value").GetString();
        Assert.Equal(SiteClientSecret.Derive(_secrets.Platform, "site-purchasing"), secret);

        await SyncAsync(realm, _secrets.Activation, state);

        var again = (await GetJsonAsync(token, $"admin/realms/{realm}/clients/{uuid}/client-secret")).GetProperty("value").GetString();
        Assert.Equal(secret, again);
    }

    [Fact]
    public async Task A_site_client_holds_its_allowed_providers_and_accounts_flag_and_a_second_sync_updates_both()
    {
        var realm = await NewRealmAsync();
        await SyncAsync(realm, _secrets.Activation, new RealmState(
            [new RealmSiteClient("purchasing", Accounts: true, [Alias]), new RealmSiteClient("service-desk", Accounts: false, [Alias])],
            [Provider()]));

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var purchasing = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing"))[0].GetProperty("attributes");
        Assert.Equal(Alias, purchasing.GetProperty(RealmSync.ProvidersAttribute).GetString());
        Assert.Equal("true", purchasing.GetProperty(RealmSync.AccountsAttribute).GetString());
        var serviceDesk = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-service-desk"))[0].GetProperty("attributes");
        Assert.Equal("false", serviceDesk.GetProperty(RealmSync.AccountsAttribute).GetString());

        await SyncAsync(realm, _secrets.Activation, new RealmState(
            [new RealmSiteClient("purchasing", Accounts: false, []), new RealmSiteClient("service-desk", Accounts: false, [Alias])],
            [Provider()]));

        var changed = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing"))[0].GetProperty("attributes");
        Assert.Equal("false", changed.GetProperty(RealmSync.AccountsAttribute).GetString());
        // Keycloak stores an empty attribute as no attribute.
        Assert.True(!changed.TryGetProperty(RealmSync.ProvidersAttribute, out var providers) || string.IsNullOrEmpty(providers.GetString()));
    }

    [Fact]
    public async Task The_sign_in_page_of_a_site_client_carries_the_providers_and_accounts_flag_of_the_site()
    {
        var realm = await NewRealmAsync();
        await SyncAsync(realm, _secrets.Activation, new RealmState([new RealmSiteClient("purchasing", Accounts: false, [Alias])], [Provider()]));
        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var site = (await GetJsonAsync(token, $"admin/realms/{realm}/clients?clientId=site-purchasing"))[0];
        // The site client copies the redirect addresses of the platform client, which has none here, so give it one.
        var body = JsonNode.Parse(site.GetRawText())!.AsObject();
        body["redirectUris"] = new JsonArray("http://localhost/cb");
        using (var put = await SendAsync(token, HttpMethod.Put, $"admin/realms/{realm}/clients/{site.GetProperty("id").GetString()}", body))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var page = await browser.GetAsync(
            new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/auth?client_id=site-purchasing&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString("http://localhost/cb")}"),
            TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        var match = SignInContextPattern().Match(html);
        Assert.True(match.Success, html);
        using var context = JsonDocument.Parse(match.Groups["context"].Value);
        var siteContext = context.RootElement.GetProperty("site");
        Assert.Equal([Alias], siteContext.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));
        Assert.False(siteContext.GetProperty("accounts").GetBoolean());
    }

    [Fact]
    public async Task A_provider_label_is_written_in_each_locale_and_removed_with_the_provider()
    {
        var realm = await NewRealmAsync();
        var labels = new Dictionary<string, string> { ["en"] = "Company", ["vi"] = "Công ty" };
        await SyncAsync(realm, _secrets.Activation, State([], [Provider() with { Labels = labels }]));

        var token = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        var key = RealmSync.LabelKey(Alias);
        Assert.Equal("Company", await LabelAsync(token, realm, "en", key));
        Assert.Equal("Công ty", await LabelAsync(token, realm, "vi", key));

        await SyncAsync(realm, _secrets.Activation, State([], [Provider() with { Labels = new Dictionary<string, string> { ["en"] = "Firm", ["vi"] = "Hãng" } }]));

        Assert.Equal("Firm", await LabelAsync(token, realm, "en", key));

        await SyncAsync(realm, _secrets.Activation, State([], []));

        Assert.Null(await LabelAsync(token, realm, "en", key));
        Assert.Null(await LabelAsync(token, realm, "vi", key));
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
    public async Task Syncing_the_same_state_twice_leaves_one_client_per_site_and_one_provider_with_five_mappers()
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
        Assert.Equal(5, (await MapperNamesAsync(token, realm)).Count);
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

    [GeneratedRegex("""<script type="application/json" id="axis-sign-in-context">(?<context>.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex SignInContextPattern();

    private RealmBrokeredProvider Provider() =>
        new(Alias, new Uri(_baseAddress, "realms/master"), "company-client", "company-secret", "name", "email", "locale", ["groups"]);

    private static RealmState State(string[] sitePaths, RealmBrokeredProvider[] providers) =>
        new([.. sitePaths.Select(path => new RealmSiteClient(path, Accounts: true, []))], providers);

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

    private async Task<string?> LabelAsync(string token, string realm, string locale, string key) =>
        (await GetJsonAsync(token, $"admin/realms/{realm}/localization/{locale}")).TryGetProperty(key, out var text) ? text.GetString() : null;

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
