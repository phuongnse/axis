using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in that starts on a site page, against Keycloak. Tenant <c>a</c> has realm details, and its startup activation syncs the
/// site clients of its application into the realm. In the development realm the application has two sites that allow accounts,
/// <c>sitesaccounts</c> and <c>sitesother</c>, as that realm has no flow for a brokered provider. In a realm of its own the application
/// has <c>sitespartner</c> as well, which allows only the brokered provider <c>partner</c>. The tests show the client of each
/// sign-in, the provider hint and the check of the ID token's audience.
/// </summary>
public sealed class SiteSignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string SiteIssuer = "SiteIssuer";
    private const string SiteClient = "SiteClient";
    private const string SiteSecret = "SiteSecret";

    // The realm's client accepts redirects to http://localhost:5206/*, the development server, and so do the site clients.
    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private static readonly TenantRealmSecrets _realmSecrets = new("platform-1", "activation-1", "users-1");

    private string? _schemaConnection;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sign_in_for_a_site_goes_to_the_realm_as_the_site_client_and_completes_a_session_through_it()
    {
        await using var host = await StartInDevelopmentRealmAsync();

        var callback = await KeycloakSignIn.LogInAsync(host.Client, host.Authority, "requester", _serverAddress, returnUrl: "/sitesaccounts");
        using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal("/sitesaccounts", signedIn.Headers.Location!.OriginalString);
        using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(1L, await CountAsync("axis.sessions"));
        Assert.Equal(1L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
    }

    [Fact]
    public async Task A_sign_in_for_a_site_redirects_with_the_site_client_and_without_a_provider_hint_when_the_site_allows_accounts()
    {
        await using var host = await StartInDevelopmentRealmAsync();

        var query = await host.SignInQueryAsync("/sitesaccounts/orders?x=1");

        Assert.Equal("site-sitesaccounts", query["client_id"]);
        Assert.Null(query["kc_idp_hint"]);
    }

    [Fact]
    public async Task A_sign_in_for_a_site_with_one_provider_and_no_accounts_names_that_provider()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        var realm = $"t{Guid.NewGuid():N}"[..16];
        using var http = new HttpClient();
        try
        {
            await new TenantRealm(http, new IdentityProviderAdmin(baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
                .EnsureAsync(realm, "Acme", _realmSecrets, CancellationToken);
            await using var host = await StartAsync(
                partner: true, new Uri(baseAddress, $"realms/{realm}"), realm, _realmSecrets.Platform, _realmSecrets.Activation);

            var partner = await host.SignInQueryAsync("/sitespartner");
            var accounts = await host.SignInQueryAsync("/sitesaccounts");

            Assert.Equal("site-sitespartner", partner["client_id"]);
            Assert.Equal("oidc-siteissuer", partner["kc_idp_hint"]);
            Assert.Equal("site-sitesaccounts", accounts["client_id"]);
            Assert.Null(accounts["kc_idp_hint"]);
        }
        finally
        {
            await DeleteRealmAsync(http, baseAddress, realm);
        }
    }

    [Theory]
    [InlineData("/studio")]
    [InlineData("/")]
    [InlineData("/nosuchsite/page")]
    public async Task A_sign_in_whose_return_address_is_outside_any_site_keeps_the_platform_client(string returnUrl)
    {
        await using var host = await StartInDevelopmentRealmAsync();

        var query = await host.SignInQueryAsync(returnUrl);

        Assert.Equal(KeycloakFixture.ClientId, query["client_id"]);
        Assert.Null(query["kc_idp_hint"]);
    }

    [Fact]
    public async Task An_id_token_for_another_sites_client_is_refused_and_writes_no_session()
    {
        using var provider = new IdTokenRewriteHandler(editIdToken: claims => claims["aud"] = "site-sitesother");
        await using var host = await StartInDevelopmentRealmAsync(provider);
        var callback = await KeycloakSignIn.LogInAsync(host.Client, host.Authority, "requester", _serverAddress, returnUrl: "/sitesaccounts");

        using var refused = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

        Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
        Assert.Equal("/?signIn=failed", refused.Headers.Location?.OriginalString);
        Assert.Equal(0L, await CountAsync("axis.sessions"));
        Assert.Equal(0L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
        var details = (string)(await PostgreSqlServer.AdminScalarAsync(
            _schemaConnection!, "SELECT details::text FROM axis.audit_records WHERE action = 'session.signInRefused'", CancellationToken))!;
        using var document = JsonDocument.Parse(details);
        Assert.Equal("invalidToken", document.RootElement.GetProperty("reason").GetString());
        using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    public async ValueTask InitializeAsync() =>
        _schemaConnection = await database.CreateDatabaseAsync("sitesignin", CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string Site(string id, string path, string provider) =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "{{path}}", "formatVersion": 1, "path": "{{path}}", "signIn": ["{{provider}}"],
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "Home", "label": { "textKey": "nav.home" } } ] }
        """;

    /// <summary>
    /// An application with the sites <c>sitesaccounts</c> and, as that realm has no flow for a brokered provider, <c>sitesother</c>, which allow
    /// accounts. With <paramref name="partner"/> it has the site <c>sitespartner</c> too, which allows only the provider <c>partner</c>.
    /// </summary>
    private static TemporaryFolder Applications(bool partner)
    {
        var folder = new TemporaryFolder()
            .With("application.json", """{ "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b10", "kind": "application", "name": "SiteSignIn", "formatVersion": 1 }""")
            .With("texts/en.json", """
                { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b11", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "site.title": "Home", "nav.home": "Home", "pages.home.title": "Home", "signIn.partner": "Continue with Partner" } }
                """)
            .With("pages/home.json", """
                { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b12", "kind": "page", "name": "Home", "formatVersion": 1,
                  "title": { "textKey": "pages.home.title" }, "widgets": [ { "type": "taskInbox" } ] }
                """)
            .With("identity-providers/accounts.json", """{ "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b13", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""")
            .With("sites/sitesaccounts.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b15", "sitesaccounts", "accounts"));
        return partner
            ? folder
                .With("identity-providers/partner.json", $$"""
                    { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b14", "kind": "identityProvider", "name": "partner", "formatVersion": 1, "type": "oidc",
                      "label": { "textKey": "signIn.partner" },
                      "issuer": { "binding": "{{SiteIssuer}}" }, "clientId": { "binding": "{{SiteClient}}" },
                      "clientSecret": { "binding": "{{SiteSecret}}" } }
                    """)
                .With("sites/sitespartner.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b16", "sitespartner", "partner"))
            : folder.With("sites/sitesother.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b17", "sitesother", "accounts"));
    }

    /// <summary>
    /// Starts the server in front of the shared development realm. Its startup activation leaves the realm with the clients of
    /// the sites <c>sitesaccounts</c> and <c>sitesother</c>.
    /// </summary>
    private async Task<SiteHost> StartInDevelopmentRealmAsync(IdTokenRewriteHandler? provider = null) =>
        await StartAsync(
            partner: false, await keycloak.StartAsync(), "axis", KeycloakFixture.ClientSecret, KeycloakFixture.ActivationClientSecret, provider);

    /// <summary>Starts the server for tenant <c>a</c>, which has the details of <paramref name="realm"/>. Starting it runs the activation, which syncs the realm.</summary>
    private async Task<SiteHost> StartAsync(
        bool partner,
        Uri authority,
        string realm,
        string platformSecret,
        string activationSecret,
        IdTokenRewriteHandler? provider = null)
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        var applications = Applications(partner);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_schemaConnection!);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_schemaConnection!));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _schemaConnection!);
            builder.UseSetting("Tenants:a:Oidc:Authority", authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", platformSecret);
            builder.UseSetting("Tenants:a:Realm:Name", realm);
            builder.UseSetting("Tenants:a:Realm:ActivationClientSecret", activationSecret);
            builder.UseSetting($"Tenants:a:Bindings:{SiteIssuer}:Kind", "url");
            builder.UseSetting($"Tenants:a:Bindings:{SiteIssuer}:Value", new Uri(baseAddress, "realms/master").AbsoluteUri);
            builder.UseSetting($"Tenants:a:Bindings:{SiteClient}:Kind", "secret");
            builder.UseSetting($"Tenants:a:Bindings:{SiteClient}:Value", "site-client-value");
            builder.UseSetting($"Tenants:a:Bindings:{SiteSecret}:Kind", "secret");
            builder.UseSetting($"Tenants:a:Bindings:{SiteSecret}:Value", "site-secret-value");
            builder.UseSetting("ActivateOnStartup:0", applications.Path);
            if (provider is not null)
            {
                builder.ConfigureTestServices(services =>
                    services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName("a"), options => options.BackchannelHttpHandler = provider));
            }
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        return new SiteHost(factory, client, applications, authority);
    }

    private async Task<long> CountAsync(string from) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_schemaConnection!, $"SELECT count(*) FROM {from}", CancellationToken))!;

    private static async Task DeleteRealmAsync(HttpClient http, Uri baseAddress, string realm)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var tokenResponse = await http.PostAsync(new Uri(baseAddress, "realms/master/protocol/openid-connect/token"), form, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("access_token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(baseAddress, $"admin/realms/{realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    private sealed class SiteHost(WebApplicationFactory<Program> factory, HttpClient client, TemporaryFolder applications, Uri authority) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public Uri Authority => authority;

        /// <summary>Starts a sign-in with <paramref name="returnUrl"/> and returns the query of the address that the server redirects to.</summary>
        public async Task<System.Collections.Specialized.NameValueCollection> SignInQueryAsync(string returnUrl)
        {
            using var challenge = await client.GetAsync(new Uri($"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
            var authorize = challenge.Headers.Location!;
            Assert.StartsWith($"{authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
            return HttpUtility.ParseQueryString(authorize.Query);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
            applications.Dispose();
        }
    }
}
