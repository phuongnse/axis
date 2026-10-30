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
/// has <c>sitespartner</c> and <c>sitespartner2</c> as well, which allow only the brokered provider <c>partner</c>. With a SAML issuer
/// it has the <c>saml</c> provider <c>agency</c> and the site <c>sitesagency</c>, which allows only it. The tests show
/// the client of each sign-in, the provider hint, the check of the ID token's audience, the provider that the session row keeps
/// and the sites that serve a session through that provider.
/// </summary>
public sealed class SiteSignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string SiteIssuer = "SiteIssuer";
    private const string SiteClient = "SiteClient";
    private const string SiteSecret = "SiteSecret";
    private const string SamlMetadata = "AgencyMetadata";

    // The realm's client accepts redirects to http://localhost:5206/*, the development server, and so do the site clients.
    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private static readonly string _callbackAddress = new Uri(_serverAddress, TenantSignIn.CallbackPath).AbsoluteUri;

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
        Assert.Null(await SessionIdentityProviderAsync());
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
        Assert.Equal("invalidToken", await RefusalReasonAsync());
        using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task A_site_callback_through_a_provider_the_site_does_not_allow_is_refused_even_when_kc_idp_hint_forces_it()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress);
            await using var hosted = host;
            var authorize = await host.AuthorizeAsync("/sitesaccounts");
            var query = HttpUtility.ParseQueryString(authorize.Query);
            Assert.Equal("site-sitesaccounts", query["client_id"]);
            Assert.Null(query["kc_idp_hint"]);

            // The site allows accounts only, but the browser forces the brokered provider on the realm's authorize address.
            var forced = new Uri($"{authorize.AbsoluteUri}&kc_idp_hint=oidc-siteissuer");
            var callback = await realms.LogInAsync(forced, upstream, _callbackAddress);
            using var refused = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
            Assert.Equal("/?signIn=failed", refused.Headers.Location?.OriginalString);
            Assert.Equal(0L, await CountAsync("axis.sessions"));
            Assert.Equal(0L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
            Assert.Equal("providerNotAllowed", await RefusalReasonAsync());
            using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_sign_in_through_a_saml_provider_creates_a_session_with_the_mapped_name_and_email()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartSamlAsync(realms, baseAddress);
            await using var hosted = host;

            var authorize = await host.AuthorizeAsync("/sitesagency");
            Assert.Equal("saml-agencymetadata", HttpUtility.ParseQueryString(authorize.Query)["kc_idp_hint"]);
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
            Assert.Equal("/sitesagency", signedIn.Headers.Location!.OriginalString);
            Assert.Equal(1L, await CountAsync("axis.sessions"));
            Assert.Equal(1L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
            Assert.Equal("saml-agencymetadata", await SessionIdentityProviderAsync());
            using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            var body = await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal("Pat", body.GetProperty("displayName").GetString());
            Assert.Equal(
                UpstreamRealms.Email,
                await PostgreSqlServer.AdminScalarAsync(_schemaConnection!, "SELECT email FROM axis.user_profiles", CancellationToken));
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_sign_in_through_a_saml_provider_gives_the_roles_that_the_roles_mapping_names()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartSamlAsync(realms, baseAddress, groups: "purchasing-managers");
            await using var hosted = host;

            var authorize = await host.AuthorizeAsync("/sitesagency");
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
            Assert.Equal("/sitesagency", signedIn.Headers.Location!.OriginalString);
            Assert.Equal("""{"3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b10": ["buyer"]}""", await SessionApplicationRolesAsync());
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_sign_in_through_an_oidc_provider_gives_the_roles_that_the_roles_mapping_names()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress, groups: "purchasing-managers");
            await using var hosted = host;

            var authorize = await host.AuthorizeAsync("/sitespartner");
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
            Assert.Equal("/sitespartner", signedIn.Headers.Location!.OriginalString);
            Assert.Equal("""{"3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b10": ["buyer"]}""", await SessionApplicationRolesAsync());
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_sign_in_with_a_roles_value_that_the_mapping_does_not_list_gives_no_role()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress, groups: "visitors");
            await using var hosted = host;

            var authorize = await host.AuthorizeAsync("/sitespartner");
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
            Assert.Equal(1L, await CountAsync("axis.sessions"));
            Assert.Equal("{}", await SessionApplicationRolesAsync());
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_callback_through_a_saml_provider_the_site_does_not_allow_is_refused()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartSamlAsync(realms, baseAddress);
            await using var hosted = host;
            var authorize = await host.AuthorizeAsync("/sitesaccounts");
            Assert.Null(HttpUtility.ParseQueryString(authorize.Query)["kc_idp_hint"]);

            // The site allows accounts only, but the browser forces the SAML provider on the realm's authorize address.
            var forced = new Uri($"{authorize.AbsoluteUri}&kc_idp_hint=saml-agencymetadata");
            var callback = await realms.LogInAsync(forced, upstream, _callbackAddress);
            using var refused = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
            Assert.Equal("/?signIn=failed", refused.Headers.Location?.OriginalString);
            Assert.Equal(0L, await CountAsync("axis.sessions"));
            Assert.Equal(0L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
            Assert.Equal("providerNotAllowed", await RefusalReasonAsync());
            using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_callback_through_a_provider_the_site_allows_creates_a_session()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress);
            await using var hosted = host;

            // The site allows only the provider, so the server names it in the authorize address itself.
            var authorize = await host.AuthorizeAsync("/sitespartner");
            Assert.Equal("oidc-siteissuer", HttpUtility.ParseQueryString(authorize.Query)["kc_idp_hint"]);
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
            Assert.Equal("/sitespartner", signedIn.Headers.Location!.OriginalString);
            Assert.Equal(1L, await CountAsync("axis.sessions"));
            Assert.Equal(1L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
            Assert.Equal("oidc-siteissuer", await SessionIdentityProviderAsync());
            using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_session_through_a_provider_opens_a_second_site_that_allows_it_and_a_site_that_does_not_asks_to_sign_in_again()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress);
            await using var hosted = host;
            var callback = await realms.LogInAsync(await host.AuthorizeAsync("/sitespartner"), upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);
            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);

            // The second site allows the same provider, so it serves the session with no new callback.
            using var allowed = await host.Client.GetAsync(new Uri("/api/sites/sitespartner2", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using var allowedPage = await host.Client.GetAsync(new Uri("/api/sites/sitespartner2/pages/Home", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowedPage.StatusCode);

            // The third site allows only accounts, so every read of it asks for a new sign-in. An unknown site is still 404.
            foreach (var refused in new[] { "/api/sites/sitesaccounts", "/api/sites/sitesaccounts/pages/Home", "/api/sites/sitesaccounts/texts/en" })
            {
                using var response = await host.Client.GetAsync(new Uri(refused, UriKind.Relative), CancellationToken);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            using var unknown = await host.Client.GetAsync(new Uri("/api/sites/nosuchsite", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

            // The sign-in for the refused site asks the realm for credentials. The sign-in for an allowed site does not.
            Assert.Equal("login", (await host.SignInQueryAsync("/sitesaccounts"))["prompt"]);
            Assert.Null((await host.SignInQueryAsync("/sitespartner2"))["prompt"]);
            Assert.Equal(1L, await CountAsync("axis.sessions"));
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_callback_whose_upstream_acr_the_provider_does_not_accept_is_refused()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress, partnerAcr: ["urn:axis:test:high"]);
            await using var hosted = host;

            var authorize = await host.AuthorizeAsync("/sitespartner");
            Assert.Equal("urn:axis:test:high", HttpUtility.ParseQueryString(authorize.Query)["acr_values"]);
            // A new login at the upstream realm gives the acr "1", which the provider's list leaves out.
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var refused = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
            Assert.Equal("/?signIn=failed", refused.Headers.Location?.OriginalString);
            Assert.Equal(0L, await CountAsync("axis.sessions"));
            Assert.Equal(0L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
            Assert.Equal("acrNotAccepted", await RefusalReasonAsync());
            using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    [Fact]
    public async Task A_site_callback_whose_upstream_acr_is_in_the_providers_list_creates_a_session()
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            var (host, upstream) = await StartBrokeredAsync(realms, baseAddress, partnerAcr: ["1"]);
            await using var hosted = host;

            var authorize = await host.AuthorizeAsync("/sitespartner");
            Assert.Equal("1", HttpUtility.ParseQueryString(authorize.Query)["acr_values"]);
            var callback = await realms.LogInAsync(authorize, upstream, _callbackAddress);
            using var signedIn = await host.Client.GetAsync(callback.PathAndQuery, CancellationToken);

            Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
            Assert.Equal("/sitespartner", signedIn.Headers.Location!.OriginalString);
            Assert.Equal(1L, await CountAsync("axis.sessions"));
            Assert.Equal(1L, await CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
            using var me = await host.Client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }
    }

    public async ValueTask InitializeAsync() =>
        _schemaConnection = await database.CreateDatabaseAsync("sitesignin", CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Starts the server in front of a tenant realm of its own, with the sites of <see cref="Applications"/> and the provider
    /// <c>partner</c> bound to an upstream realm that holds the user <c>pat</c>. The realm's platform client accepts redirects to the
    /// server before the server starts, so the site clients that its activation makes accept them too. Returns the host and the
    /// upstream realm. A <paramref name="partnerAcr"/> list becomes the <c>acr</c> of the provider.
    /// </summary>
    private async Task<(SiteHost Host, string Upstream)> StartBrokeredAsync(UpstreamRealms realms, Uri baseAddress, string[]? partnerAcr = null, string? groups = null)
    {
        var tenant = await NewTenantRealmAsync(realms, baseAddress);
        var upstream = await realms.NewUpstreamRealmAsync(groups);
        await realms.AddUpstreamUserAsync(upstream);
        var host = await StartAsync(
            partner: true,
            new Uri(baseAddress, $"realms/{tenant}"),
            tenant,
            _realmSecrets.Platform,
            _realmSecrets.Activation,
            siteIssuer: new Uri(baseAddress, $"realms/{upstream}"),
            siteClient: UpstreamRealms.ClientId,
            siteSecret: UpstreamRealms.BrokerSecret,
            partnerAcr: partnerAcr,
            roles: groups is not null);
        await realms.PointBackChannelAtTheContainerAsync(tenant, "oidc-siteissuer", upstream);
        return (host, upstream);
    }

    /// <summary>
    /// Starts the server in front of a tenant realm of its own, with the sites of <see cref="Applications"/> and the <c>saml</c> provider
    /// <c>agency</c> bound to the metadata of an upstream SAML realm that holds the user <c>pat</c>. Returns the host and the upstream realm.
    /// </summary>
    private async Task<(SiteHost Host, string Upstream)> StartSamlAsync(UpstreamRealms realms, Uri baseAddress, string? groups = null)
    {
        var tenant = await NewTenantRealmAsync(realms, baseAddress);
        var entityId = $"{baseAddress.AbsoluteUri}realms/{tenant}";
        var upstream = await realms.NewUpstreamSamlRealmAsync(entityId, $"{entityId}/broker/saml-agencymetadata/endpoint", groups);
        await realms.AddUpstreamUserAsync(upstream);
        var host = await StartAsync(
            partner: false,
            new Uri(baseAddress, $"realms/{tenant}"),
            tenant,
            _realmSecrets.Platform,
            _realmSecrets.Activation,
            samlMetadata: realms.SamlMetadata(upstream),
            roles: groups is not null);
        return (host, upstream);
    }

    /// <summary>Makes a tenant realm whose platform client accepts redirects to the server, before the server starts and makes the site clients.</summary>
    private static async Task<string> NewTenantRealmAsync(UpstreamRealms realms, Uri baseAddress)
    {
        var tenant = realms.NewRealmName();
        using var http = new HttpClient();
        await new TenantRealm(http, new IdentityProviderAdmin(baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(tenant, "Acme", _realmSecrets, CancellationToken);
        var clients = await realms.GetJsonAsync($"admin/realms/{tenant}/clients?clientId={TenantRealm.PlatformClientId}");
        var platform = clients[0].Deserialize<Dictionary<string, JsonElement>>()!;
        platform["redirectUris"] = JsonSerializer.SerializeToElement(new[] { $"{_serverAddress.AbsoluteUri}*" });
        using var updated = await realms.SendAsync(HttpMethod.Put, $"admin/realms/{tenant}/clients/{platform["id"].GetString()}", platform);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        return tenant;
    }

    private async Task<string> RefusalReasonAsync()
    {
        var details = (string)(await PostgreSqlServer.AdminScalarAsync(
            _schemaConnection!, "SELECT details::text FROM axis.audit_records WHERE action = 'session.signInRefused'", CancellationToken))!;
        using var document = JsonDocument.Parse(details);
        return document.RootElement.GetProperty("reason").GetString()!;
    }

    private static string Site(string id, string path, string provider) =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "{{path}}", "formatVersion": 1, "path": "{{path}}", "signIn": ["{{provider}}"],
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "Home", "label": { "textKey": "nav.home" } } ] }
        """;

    /// <summary>
    /// An application with the sites <c>sitesaccounts</c> and, as that realm has no flow for a brokered provider, <c>sitesother</c>, which allow
    /// accounts. With <paramref name="partner"/> it has the sites <c>sitespartner</c> and <c>sitespartner2</c> too, which allow only the provider <c>partner</c>,
    /// and a <paramref name="partnerAcr"/> list is the provider's <c>acr</c>. With <paramref name="saml"/> it has the <c>saml</c> provider
    /// <c>agency</c> and the site <c>sitesagency</c>, which allows only it. With <paramref name="roles"/> both providers map the value
    /// <c>purchasing-managers</c> of the claim <c>groups</c> to the role <c>buyer</c>.
    /// </summary>
    private static TemporaryFolder Applications(bool partner, string[]? partnerAcr = null, bool saml = false, bool roles = false)
    {
        var rolesMapping = roles ? """, "roles": { "claim": "groups", "values": { "purchasing-managers": "buyer" } }""" : "";
        var folder = new TemporaryFolder()
            .With("application.json", """{ "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b10", "kind": "application", "name": "SiteSignIn", "formatVersion": 1 }""")
            .With("texts/en.json", """
                { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b11", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "site.title": "Home", "nav.home": "Home", "pages.home.title": "Home", "signIn.partner": "Continue with Partner", "signIn.agency": "Continue with Agency", "role.buyer": "Buyer" } }
                """)
            .With("pages/home.json", """
                { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b12", "kind": "page", "name": "Home", "formatVersion": 1,
                  "title": { "textKey": "pages.home.title" }, "widgets": [ { "type": "taskInbox" } ] }
                """)
            .With("identity-providers/accounts.json", """{ "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b13", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""")
            .With("sites/sitesaccounts.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b15", "sitesaccounts", "accounts"));
        if (roles)
        {
            folder.With("roles/buyer.json", """{ "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b1b", "kind": "role", "name": "buyer", "formatVersion": 1, "label": { "textKey": "role.buyer" } }""");
        }

        if (saml)
        {
            folder
                .With("identity-providers/agency.json", $$"""
                    { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b19", "kind": "identityProvider", "name": "agency", "formatVersion": 1, "type": "saml",
                      "label": { "textKey": "signIn.agency" }, "metadata": { "binding": "{{SamlMetadata}}" }{{rolesMapping}} }
                    """)
                .With("sites/sitesagency.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b1a", "sitesagency", "agency"));
        }

        var acr = partnerAcr is null ? "" : $$""", "acr": [{{string.Join(", ", partnerAcr.Select(level => $"\"{level}\""))}}]""";
        return partner
            ? folder
                .With("identity-providers/partner.json", $$"""
                    { "id": "3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b14", "kind": "identityProvider", "name": "partner", "formatVersion": 1, "type": "oidc",
                      "label": { "textKey": "signIn.partner" },
                      "issuer": { "binding": "{{SiteIssuer}}" }, "clientId": { "binding": "{{SiteClient}}" },
                      "clientSecret": { "binding": "{{SiteSecret}}" }{{acr}}{{rolesMapping}} }
                    """)
                .With("sites/sitespartner.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b16", "sitespartner", "partner"))
                .With("sites/sitespartner2.json", Site("3c6b1f52-8d0e-4a47-9c1b-2f7e5a9d4b18", "sitespartner2", "partner"))
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
        IdTokenRewriteHandler? provider = null,
        Uri? siteIssuer = null,
        string siteClient = "site-client-value",
        string siteSecret = "site-secret-value",
        string[]? partnerAcr = null,
        Uri? samlMetadata = null,
        bool roles = false)
    {
        var baseAddress = await keycloak.BaseAddressAsync();
        var applications = Applications(partner, partnerAcr, saml: samlMetadata is not null, roles);
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
            builder.UseSetting($"Tenants:a:Bindings:{SiteIssuer}:Value", (siteIssuer ?? new Uri(baseAddress, "realms/master")).AbsoluteUri);
            builder.UseSetting($"Tenants:a:Bindings:{SiteClient}:Kind", "secret");
            builder.UseSetting($"Tenants:a:Bindings:{SiteClient}:Value", siteClient);
            builder.UseSetting($"Tenants:a:Bindings:{SiteSecret}:Kind", "secret");
            builder.UseSetting($"Tenants:a:Bindings:{SiteSecret}:Value", siteSecret);
            if (samlMetadata is not null)
            {
                builder.UseSetting($"Tenants:a:Bindings:{SamlMetadata}:Kind", "url");
                builder.UseSetting($"Tenants:a:Bindings:{SamlMetadata}:Value", samlMetadata.AbsoluteUri);
            }

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

    private async Task<string?> SessionIdentityProviderAsync() =>
        await PostgreSqlServer.AdminScalarAsync(_schemaConnection!, "SELECT identity_provider FROM axis.sessions", CancellationToken) as string;

    private async Task<string> SessionApplicationRolesAsync() =>
        (string)(await PostgreSqlServer.AdminScalarAsync(_schemaConnection!, "SELECT application_roles::text FROM axis.sessions", CancellationToken))!;

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

        /// <summary>
        /// Starts a sign-in with <paramref name="returnUrl"/> on the server and returns the realm's authorize address. The
        /// server's client keeps the correlation and nonce cookies for the callback.
        /// </summary>
        public async Task<Uri> AuthorizeAsync(string returnUrl)
        {
            using var challenge = await client.GetAsync(new Uri($"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
            return challenge.Headers.Location!;
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
            applications.Dispose();
        }
    }
}
