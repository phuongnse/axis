using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>activate</c> on a real Keycloak: after the activation commits, the tenant's realm gets one <c>site-</c> client for each
/// site and one <c>oidc-</c> provider for each issuer binding of the tenant's applications and of its platform sign-in. A refused
/// activation, a preview tenant and a realm failure each leave the realm as it was. <c>tenant sign-in set</c> shares a provider with
/// the applications that name the same bindings, refuses one that would split a person, and activation keeps a provider that only the
/// platform sign-in names.
/// </summary>
public sealed class AdminActivateRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Issuer = "Issuer";
    private const string Client = "Client";
    private const string Secret = "Secret";

    private static readonly string[] _mapperNames = ["display name", "email", "locale", "username"];

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
    public async Task Activate_gives_the_realm_a_client_for_each_site_and_a_provider_with_its_mappers_for_the_oidc_provider()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        using var folder = Folder(new Application(), ["sales", "support"], provider: true);

        var result = await ActivateAsync(scenario, folder, id);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["version", "compile", "activate", "seed", "indexes"], result.Output.Select(line => line.Split(' ')[1]));
        Assert.Equal(["site-sales", "site-support"], await SiteClientsAsync(id));
        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(id));
        var mappers = await MapperNamesAsync(id, "oidc-issuer");
        Assert.All(_mapperNames, name => Assert.Contains(name, mappers));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activating_the_application_again_without_the_provider_removes_the_provider_from_the_realm()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        var application = new Application();
        using (var first = Folder(application, ["sales"], provider: true))
        {
            Assert.Equal(0, (await ActivateAsync(scenario, first, id)).Exit);
        }

        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(id));
        using var second = Folder(application, ["sales"], provider: false);

        var result = await ActivateAsync(scenario, second, id);

        Assert.Equal(0, result.Exit);
        Assert.Empty(await ProviderAliasesAsync(id));
        Assert.Equal(["site-sales"], await SiteClientsAsync(id));
    }

    [Fact]
    public async Task A_second_application_that_conflicts_fails_activate_with_the_diagnostic_and_leaves_the_realm_unchanged()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        using (var first = Folder(new Application(), ["sales"], provider: true))
        {
            Assert.Equal(0, (await ActivateAsync(scenario, first, id)).Exit);
        }

        var clients = await SiteClientsAsync(id);
        var providers = await ProviderAliasesAsync(id);
        await SetBindingsAsync(scenario, id, "Other");
        using var second = Folder(new Application(), ["other"], provider: true, clientBinding: "OtherClient", secretBinding: "OtherSecret");

        var result = await ActivateAsync(scenario, second, id);

        Assert.Equal(1, result.Exit);
        Assert.Contains(result.Output, line => line.StartsWith($"{id} activate failed: AXC0291 ", StringComparison.Ordinal));
        Assert.Equal(clients, await SiteClientsAsync(id));
        Assert.Equal(providers, await ProviderAliasesAsync(id));
    }

    [Fact]
    public async Task Activate_on_a_preview_tenant_passes_with_a_warning_and_leaves_the_main_tenants_realm_without_a_provider()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var main = await CreateTenantWithBindingsAsync(scenario);
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, $"{preview}.example.test"), identityProvider: _handler)).Exit);
        using var folder = Folder(new Application(), ["sales"], provider: true);

        var result = await ActivateAsync(scenario, folder, preview);

        Assert.Equal(0, result.Exit);
        Assert.Contains(result.Error, line => line.Contains("AXC0100", StringComparison.Ordinal));
        Assert.Empty(await ProviderAliasesAsync(main));
        Assert.Empty(await SiteClientsAsync(main));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_wrong_activation_secret_fails_the_activate_step_with_a_line_that_names_no_secret()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        var wrong = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.RealmActivationClientSecret, $"wrong-{Guid.NewGuid():N}");
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            await context.Tenants.Where(tenant => tenant.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(tenant => tenant.RealmActivationClientSecret, wrong), CancellationToken);
        }

        using var folder = Folder(new Application(), ["sales"], provider: true);

        var result = await ActivateAsync(scenario, folder, id);

        Assert.Equal(1, result.Exit);
        Assert.Contains(result.Output, line => line.StartsWith($"{id} activate failed: The identity provider step", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output, line => line.Contains(" seed ", StringComparison.Ordinal));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_platform_provider_with_the_bindings_of_an_application_provider_in_other_letter_case_shares_its_realm_provider()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        using (var folder = Folder(new Application(), ["sales"], provider: true))
        {
            Assert.Equal(0, (await ActivateAsync(scenario, folder, id)).Exit);
        }

        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(id));

        var result = await SetPlatformProviderAsync(scenario, id, "issuer", "client", "secret");

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} sign-in ok"], result.Output);
        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(id));
        Assert.Equal(["site-sales"], await SiteClientsAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_platform_provider_that_clashes_with_an_active_application_provider_exits_with_2_and_changes_neither_the_directory_nor_the_realm()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        using (var folder = Folder(new Application(), ["sales"], provider: true))
        {
            Assert.Equal(0, (await ActivateAsync(scenario, folder, id)).Exit);
        }

        await SetBindingsAsync(scenario, id, "Other");
        var version = (await scenario.RowAsync(id)).Version;
        var clients = await SiteClientsAsync(id);
        var providers = await ProviderAliasesAsync(id);

        var result = await SetPlatformProviderAsync(scenario, id, Issuer, "OtherClient", "OtherSecret");

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(result.Error, line => line.StartsWith("AXC0291 ", StringComparison.Ordinal));
        Assert.Equal(version, (await scenario.RowAsync(id)).Version);
        Assert.Empty(await scenario.PlatformProvidersAsync(id));
        Assert.Equal(clients, await SiteClientsAsync(id));
        Assert.Equal(providers, await ProviderAliasesAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activating_an_application_without_oidc_providers_keeps_the_provider_that_only_the_platform_sign_in_names()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantWithBindingsAsync(scenario);
        var issuer = new Uri(_baseAddress, "realms/master").AbsoluteUri;
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, "PlatformIssuer", "--url", issuer], extra: AdminScenario.Directory)).Exit);
        Assert.Equal(0, (await SetPlatformProviderAsync(scenario, id, "PlatformIssuer", Client, Secret)).Exit);
        Assert.Equal(["oidc-platformissuer"], await ProviderAliasesAsync(id));

        using var folder = Folder(new Application(), ["sales"], provider: false);
        var result = await ActivateAsync(scenario, folder, id);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["oidc-platformissuer"], await ProviderAliasesAsync(id));
        Assert.Equal(["site-sales"], await SiteClientsAsync(id));
    }

    private Task<AdminResult> SetPlatformProviderAsync(AdminScenario scenario, string id, string issuer, string clientId, string clientSecret) =>
        scenario.RunAsync(
            [
                "tenant", "sign-in", "set", id, "--accounts", "true",
                "--provider", "partner", "--type", "oidc", "--issuer", issuer, "--client-id", clientId, "--client-secret", clientSecret,
            ],
            identityProvider: _handler,
            extra: AdminScenario.Directory);

    // Creates the tenant with its realm in the real Keycloak, and gives it the bindings of the first application.
    private async Task<string> CreateTenantWithBindingsAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(
            AdminScenario.CreateArgs(id, $"{id}.example.test"),
            identityProvider: _handler,
            extra: new Dictionary<string, string?>
            {
                ["IdentityProvider:Url"] = _baseAddress.ToString(),
                ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
                ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
            });
        Assert.Equal(0, created.Exit);
        await SetBindingsAsync(scenario, id, "");
        return id;
    }

    // The issuer is the master realm, which serves a discovery document. An http issuer is allowed for a provider.
    private async Task SetBindingsAsync(AdminScenario scenario, string id, string prefix)
    {
        if (prefix.Length == 0)
        {
            var issuer = new Uri(_baseAddress, "realms/master").AbsoluteUri;
            Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, Issuer, "--url", issuer], extra: AdminScenario.Directory)).Exit);
        }

        foreach (var name in new[] { Client, Secret })
        {
            var result = await scenario.RunAsync(
                ["tenant", "binding", "set", id, prefix + name, "--secret"], input: $"{prefix}{name}-value-{id}", extra: AdminScenario.Directory);
            Assert.Equal(0, result.Exit);
        }
    }

    private Task<AdminResult> ActivateAsync(AdminScenario scenario, TemporaryFolder folder, string id) =>
        scenario.RunAsync(["activate", folder.Path, "--tenant", id], identityProvider: _handler);

    internal static TemporaryFolder Folder(
        Application application, string[] sitePaths, bool provider, string clientBinding = Client, string secretBinding = Secret)
    {
        var folder = new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{application.Id}}", "kind": "application", "name": "{{application.Name}}", "formatVersion": 1 }""")
            .With("entities/order.json", $$"""
                { "id": "{{application.EntityId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [ { "name": "number", "type": "text", "required": true, "maxLength": 20 } ] }
                """)
            .With("texts/en.json", $$"""
                { "id": "{{application.TextId}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "provider.company": "Company" } }
                """)
            .With("pages/orders.json", $$"""
                { "id": "{{application.PageId}}", "kind": "page", "name": "Orders", "formatVersion": 1,
                  "title": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "entity": "Order" } ] }
                """)
            .With("identity-providers/accounts.json", $$"""{ "id": "{{application.AccountsId}}", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""");
        for (var index = 0; index < sitePaths.Length; index++)
        {
            folder.With($"sites/site{index}.json", $$"""
                { "id": "{{application.SiteId(index)}}", "kind": "site", "name": "Site{{index}}", "formatVersion": 1, "path": "{{sitePaths[index]}}",
                  "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
                  "navigation": [ { "page": "Orders", "label": { "textKey": "nav.orders" } } ] }
                """);
        }

        return provider
            ? folder.With("identity-providers/company.json", $$"""
                { "id": "{{application.ProviderId}}", "kind": "identityProvider", "name": "company", "formatVersion": 1, "type": "oidc",
                  "label": { "textKey": "provider.company" },
                  "issuer": { "binding": "{{Issuer}}" }, "clientId": { "binding": "{{clientBinding}}" },
                  "clientSecret": { "binding": "{{secretBinding}}" } }
                """)
            : folder;
    }

    private async Task<string[]> SiteClientsAsync(string realm)
    {
        var clients = await GetAsync(realm, "clients?max=1000");
        return [.. clients.EnumerateArray().Select(client => client.GetProperty("clientId").GetString()!).Where(clientId => clientId.StartsWith("site-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
    }

    private async Task<string[]> ProviderAliasesAsync(string realm)
    {
        var providers = await GetAsync(realm, "identity-provider/instances");
        return [.. providers.EnumerateArray().Select(provider => provider.GetProperty("alias").GetString()!).Where(alias => alias.StartsWith("oidc-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
    }

    private async Task<string[]> MapperNamesAsync(string realm, string alias)
    {
        var mappers = await GetAsync(realm, $"identity-provider/instances/{alias}/mappers");
        return [.. mappers.EnumerateArray().Select(mapper => mapper.GetProperty("name").GetString()!)];
    }

    private async Task<JsonElement> GetAsync(string realm, string path)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await TokenAsync(http, "master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var response = await SendAsync(http, admin, HttpMethod.Get, $"admin/realms/{realm}/{path}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

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

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, CancellationToken);
    }

    // The ids of one application, so a second folder of the same application is the same application.
    internal sealed class Application
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; } = $"App{Guid.NewGuid():N}";

        public Guid EntityId { get; } = Guid.NewGuid();

        public Guid TextId { get; } = Guid.NewGuid();

        public Guid PageId { get; } = Guid.NewGuid();

        public Guid AccountsId { get; } = Guid.NewGuid();

        public Guid ProviderId { get; } = Guid.NewGuid();

        private readonly Guid[] _siteIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        public Guid SiteId(int index) => _siteIds[index];
    }
}
