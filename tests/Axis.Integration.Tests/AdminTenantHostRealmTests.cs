using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant create</c> and <c>tenant host add</c>, <c>remove</c> and <c>move</c> set the redirect addresses, web origins and
/// post-logout redirect addresses of the platform client <c>axis</c> in a real Keycloak realm from the tenant's hosts, and keep its
/// other attributes. The next activation copies them to the site clients. A preview tenant, and a tenant that signs in through an
/// outside identity provider, leave every realm as it is.
/// </summary>
public sealed class AdminTenantHostRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Marker = "axis.test.marker";

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
    public async Task Create_add_move_and_remove_keep_the_addresses_of_the_platform_client_in_step_with_the_hosts_and_keep_its_other_attributes()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var other = await CreateTenantAsync(scenario);
        var first = $"{id}.example.test";
        var otherHost = $"{other}.example.test";
        var extra = $"extra-{id}.example.test";
        var third = $"third-{id}.example.test";

        // Create gives the client the addresses of its host.
        AssertAddresses(await PlatformClientAsync(id), first);
        AssertAddresses(await PlatformClientAsync(other), otherHost);
        await SetMarkerAsync(id);

        var added = await HostAsync(scenario, "add", id, extra);
        Assert.Equal([$"{id} host ok"], added.Output);
        AssertAddresses(await PlatformClientAsync(id), first, extra);

        // The host leaves one realm and enters the other.
        var moved = await HostAsync(scenario, "move", extra, "--to", other);
        Assert.Equal([$"{other} host ok"], moved.Output);
        AssertAddresses(await PlatformClientAsync(id), first);
        AssertAddresses(await PlatformClientAsync(other), otherHost, extra);

        Assert.Equal(0, (await HostAsync(scenario, "add", id, third)).Exit);
        AssertAddresses(await PlatformClientAsync(id), first, third);
        Assert.Equal(0, (await HostAsync(scenario, "remove", id, third)).Exit);
        AssertAddresses(await PlatformClientAsync(id), first);

        Assert.Equal("kept", RealmClientProfile.ReadAttributes(await PlatformClientAsync(id))[Marker]);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_host_command_rebuilds_the_addresses_that_were_changed_by_hand()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var first = $"{id}.example.test";
        await ReplaceAddressesAsync(id, "http://localhost:5206/api/auth/callback");

        Assert.Equal(0, (await HostAsync(scenario, "add", id, $"extra-{id}.example.test")).Exit);

        AssertAddresses(await PlatformClientAsync(id), first, $"extra-{id}.example.test");
    }

    [Fact]
    public async Task Activate_gives_every_site_client_the_addresses_of_the_platform_client()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        Assert.Equal(0, (await HostAsync(scenario, "add", id, $"extra-{id}.example.test")).Exit);
        using var folder = AdminActivateRealmTests.Folder(new AdminActivateRealmTests.Application(), ["sales"], provider: false);

        var activated = await scenario.RunAsync(["activate", folder.Path, "--tenant", id], identityProvider: _handler);

        Assert.Equal(0, activated.Exit);
        var platform = await PlatformClientAsync(id);
        var site = await ClientAsync(id, "site-sales");
        AssertAddresses(site, $"{id}.example.test", $"extra-{id}.example.test");
        var (platformRedirect, platformOrigins, platformPostLogout) = Lists(platform);
        var (siteRedirect, siteOrigins, sitePostLogout) = Lists(site);
        Assert.Equal(platformRedirect, siteRedirect);
        Assert.Equal(platformOrigins, siteOrigins);
        Assert.Equal(platformPostLogout, sitePostLogout);
    }

    [Fact]
    public async Task A_host_command_on_a_preview_tenant_or_a_tenant_with_an_outside_identity_provider_leaves_every_realm_unchanged()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var main = await CreateTenantAsync(scenario);
        var outside = await CreateTenantAsync(scenario);
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, $"{preview}.example.test"), identityProvider: _handler, extra: Settings())).Exit);
        await scenario.PointSignInAsync(outside, new Uri("https://idp.outside.test/realms/x"), "axis");
        var mainBefore = (await PlatformClientAsync(main)).GetRawText();
        var outsideBefore = (await PlatformClientAsync(outside)).GetRawText();

        var onPreview = await HostAsync(scenario, "add", preview, $"p2-{preview}.example.test");
        var onOutside = await HostAsync(scenario, "add", outside, $"p3-{outside}.example.test");

        Assert.Equal(0, onPreview.Exit);
        Assert.Equal(0, onOutside.Exit);
        Assert.Equal(mainBefore, (await PlatformClientAsync(main)).GetRawText());
        Assert.Equal(outsideBefore, (await PlatformClientAsync(outside)).GetRawText());
    }

    // The settings that the commands need: the directory source and the real identity provider.
    private Dictionary<string, string?> Settings() => new()
    {
        ["Tenancy:Source"] = "directory",
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    // Creates the tenant with one host and its realm in the real Keycloak.
    private async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: Settings());
        Assert.Equal(0, created.Exit);
        return id;
    }

    private Task<AdminResult> HostAsync(AdminScenario scenario, string verb, params string[] arguments) =>
        scenario.RunAsync(["tenant", "host", verb, .. arguments], identityProvider: _handler, extra: Settings());

    // The client holds exactly the addresses of the hosts, in ordinal order.
    private static void AssertAddresses(JsonElement client, params string[] hosts)
    {
        var (redirectUris, webOrigins, postLogout) = RealmSync.PlatformAddresses(hosts);
        var (actualRedirect, actualOrigins, actualPostLogout) = Lists(client);
        Assert.Equal(redirectUris, actualRedirect);
        Assert.Equal(webOrigins, actualOrigins);
        Assert.Equal(postLogout, actualPostLogout);
    }

    private static (string[] RedirectUris, string[] WebOrigins, string[] PostLogout) Lists(JsonElement client)
    {
        string[] Read(string name) => [.. client.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)];
        var postLogout = RealmClientProfile.ReadAttributes(client).GetValueOrDefault("post.logout.redirect.uris", "");
        return (Read("redirectUris"), Read("webOrigins"), [.. postLogout.Split("##", StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)]);
    }

    private Task<JsonElement> PlatformClientAsync(string realm) => ClientAsync(realm, RealmClientProfile.PlatformClientId);

    // The client as the admin API shows it, read with the master realm's admin client.
    private async Task<JsonElement> ClientAsync(string realm, string clientId)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}/clients?clientId={clientId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).EnumerateArray().Single(client => client.GetProperty("clientId").GetString() == clientId);
    }

    private async Task SetMarkerAsync(string realm) => await ChangeClientAsync(realm, client => client["attributes"]![Marker] = "kept");

    private async Task ReplaceAddressesAsync(string realm, string address) => await ChangeClientAsync(realm, client =>
    {
        client["redirectUris"] = new JsonArray(address);
        client["webOrigins"] = new JsonArray("http://localhost:5206");
        client["attributes"]!["post.logout.redirect.uris"] = "http://localhost:5206/?signedOut";
    });

    private async Task ChangeClientAsync(string realm, Action<JsonObject> change)
    {
        var current = await PlatformClientAsync(realm);
        var client = JsonNode.Parse(current.GetRawText())!.AsObject();
        client["attributes"] ??= new JsonObject();
        change(client);
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Put, $"admin/realms/{realm}/clients/{current.GetProperty("id").GetString()}", client);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
}
