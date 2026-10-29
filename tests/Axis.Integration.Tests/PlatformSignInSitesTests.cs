using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// On the tenant's host the platform sign-in callback also accepts a provider that an active site on that host allows, and
/// <c>GET /api/sites</c> lists only the sites that allow the provider of the session. Tenant <c>axis</c> has the platform
/// provider <c>company</c> and an application with three sites: <c>sitespartner</c> allows the provider <c>partner</c>,
/// <c>sitesaccounts</c> allows accounts and <c>sitesown</c> allows the provider <c>own</c> and has a host of its own. The tests
/// rewrite the ID token that Keycloak returns, so it carries the <c>identity_provider</c> claim of a brokered sign-in.
/// </summary>
public sealed class PlatformSignInSitesTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string Company = "oidc-companyissuer";
    private const string Partner = "oidc-siteissuer";
    private const string Own = "oidc-ownissuer";
    private const string RolesClaim = "axis_roles_groups";

    // The realm's client accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _address = new("http://localhost:5206");

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sign_in_through_a_platform_provider_is_accepted_on_the_tenants_host()
    {
        await using var setup = await StartAsync(Company);

        var callback = await setup.CallbackAsync();

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal(1L, await setup.CountAsync("axis.sessions"));
    }

    [Fact]
    public async Task A_sign_in_through_a_provider_that_only_a_site_on_the_tenants_host_allows_is_accepted()
    {
        await using var setup = await StartAsync(Partner);

        var callback = await setup.CallbackAsync();

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal(1L, await setup.CountAsync("axis.sessions"));
        Assert.Equal(1L, await setup.CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
    }

    [Fact]
    public async Task A_sign_in_through_a_provider_that_only_a_site_with_its_own_host_allows_is_refused_on_the_tenants_host()
    {
        await using var setup = await StartAsync(Own);

        var callback = await setup.CallbackAsync();

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal(TenantSignIn.FailedPath, callback.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            callback.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        Assert.Equal(0L, await setup.CountAsync("axis.sessions"));
        Assert.Equal("providerNotAccepted", await setup.RefusalReasonAsync());
    }

    [Fact]
    public async Task A_person_signed_in_through_a_provider_that_one_site_allows_sees_only_that_site_and_no_tool()
    {
        await using var setup = await StartAsync(Partner);
        var callback = await setup.CallbackAsync();
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);

        using var sites = await setup.Client.GetAsync("/api/sites", CancellationToken);
        using var me = await setup.Client.GetAsync("/api/me", CancellationToken);
        using var apps = await setup.Client.GetAsync("/api/studio/apps", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, sites.StatusCode);
        var list = (await sites.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("sites");
        Assert.Equal(["sitespartner"], list.EnumerateArray().Select(site => site.GetProperty("path").GetString()));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var user = await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.False(user.GetProperty("studio").GetBoolean());
        Assert.False(user.GetProperty("console").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, apps.StatusCode);
    }

    // Keycloak signs the token, so the handler re-signs it with the claims the realm sync gives a brokered sign-in.
    private static Action<JsonObject> IdToken(string identityProvider) => claims =>
    {
        claims["identity_provider"] = identityProvider;
        claims[RolesClaim] = new JsonArray("staff");
    };

    private static TenantBindingRow Url(string name, string url) => new() { TenantId = "axis", Name = name, Kind = DeploymentBindingKinds.Url, Url = url };

    private static TenantBindingRow Secret(string name, string value) =>
        new() { TenantId = "axis", Name = name, Kind = DeploymentBindingKinds.Secret, SecretValue = _cipher.Encrypt("axis", TenantSecretFields.Binding(name), value) };

    private static string Site(string id, string path, string provider) =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "{{path}}", "formatVersion": 1, "path": "{{path}}", "signIn": ["{{provider}}"],
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "Home", "label": { "textKey": "nav.home" } } ] }
        """;

    private static string OidcProvider(string id, string name, string issuerBinding) =>
        $$"""
        { "id": "{{id}}", "kind": "identityProvider", "name": "{{name}}", "formatVersion": 1, "type": "oidc",
          "label": { "textKey": "signIn.{{name}}" },
          "issuer": { "binding": "{{issuerBinding}}" }, "clientId": { "binding": "SiteClient" },
          "clientSecret": { "binding": "SiteSecret" } }
        """;

    private static TemporaryFolder Applications() =>
        new TemporaryFolder()
            .With("application.json", """{ "id": "4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c20", "kind": "application", "name": "PlatformSites", "formatVersion": 1 }""")
            .With("texts/en.json", """
                { "id": "4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c21", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "site.title": "Home", "nav.home": "Home", "pages.home.title": "Home", "signIn.partner": "Continue with Partner", "signIn.own": "Continue with Own" } }
                """)
            .With("pages/home.json", """
                { "id": "4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c22", "kind": "page", "name": "Home", "formatVersion": 1,
                  "title": { "textKey": "pages.home.title" }, "widgets": [ { "type": "taskInbox" } ] }
                """)
            .With("identity-providers/accounts.json", """{ "id": "4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c23", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""")
            .With("identity-providers/partner.json", OidcProvider("4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c24", "partner", "SiteIssuer"))
            .With("identity-providers/own.json", OidcProvider("4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c25", "own", "OwnIssuer"))
            .With("sites/sitespartner.json", Site("4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c26", "sitespartner", "partner"))
            .With("sites/sitesaccounts.json", Site("4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c27", "sitesaccounts", "accounts"))
            .With("sites/sitesown.json", Site("4d7c2a63-9e1f-4b58-8d2c-3a8f6b0e5c28", "sitesown", "own"));

    private static PlatformDbContext NewContext(string platform) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);

    // Tenant axis is the only tenant, at localhost, with realm details. Its accounts are off and the platform provider is company.
    // The site's own host is partner.localhost. The application is active before the server starts.
    private async Task<Setup> StartAsync(string identityProvider)
    {
        var authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("platform_sites_platform", CancellationToken);
        var tenant = new NpgsqlConnectionStringBuilder(await database.CreateDatabaseAsync("platform_sites_axis", CancellationToken));
        TenantBindingRow[] bindings =
        [
            Url("SiteIssuer", "https://site-issuer.example.test/realms/site"),
            Url("OwnIssuer", "https://own-issuer.example.test/realms/own"),
            Secret("SiteClient", "site-client-value"),
            Secret("SiteSecret", "site-secret-value"),
        ];
        var applications = Applications();
        await using (var connection = new NpgsqlConnection(tenant.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);

            var compiled = await ReleaseCompiler.CompileAsync(applications.Path, configuration, CancellationToken);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(
                compiled,
                new ActiveReleaseStore(configuration),
                data,
                new ActivationBindings(
                    "axis",
                    new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["SiteIssuer"] = new(ActivationBinding.UrlKind, new Uri("https://site-issuer.example.test/realms/site")),
                        ["OwnIssuer"] = new(ActivationBinding.UrlKind, new Uri("https://own-issuer.example.test/realms/own")),
                        ["SiteClient"] = new(ActivationBinding.SecretKind, null),
                        ["SiteSecret"] = new(ActivationBinding.SecretKind, null),
                    },
                    AllowHttp: false),
                OpenInstanceCounter.Instance,
                cancellationToken: CancellationToken);
            Assert.Empty(activated.Diagnostics);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(tenant.ConnectionString, CancellationToken);
        await using (var context = NewContext(platform))
        {
            await context.Database.MigrateAsync(CancellationToken);
            context.Tenants.Add(new TenantRow
            {
                Id = "axis",
                Status = "active",
                PlatformAccounts = false,
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = tenant.Host!,
                DatabasePort = tenant.Port,
                DatabaseName = tenant.Database!,
                RuntimeUser = PostgreSqlServer.RuntimeRole,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt("axis", TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
                SchemaPassword = _cipher.Encrypt("axis", TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = authority.ToString(),
                OidcClientId = KeycloakFixture.ClientId,
                OidcClientSecret = _cipher.Encrypt("axis", TenantSecretFields.OidcClientSecret, KeycloakFixture.ClientSecret),
                RealmActivationClientSecret = _cipher.Encrypt("axis", TenantSecretFields.RealmActivationClientSecret, KeycloakFixture.ActivationClientSecret),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = "axis", Host = "localhost" });
            context.TenantHosts.Add(new TenantHostRow { TenantId = "axis", Host = "partner.localhost", SitePath = "sitesown" });
            context.TenantBindings.AddRange(bindings);
            context.TenantPlatformProviders.Add(new TenantPlatformProviderRow
            {
                TenantId = "axis",
                Name = "company",
                Type = "oidc",
                IssuerBinding = "companyIssuer",
                ClientIdBinding = "companyClientId",
                ClientSecretBinding = "companySecret",
                RolesClaim = "groups",
                RoleValues = """{"axis-authors":"author"}""",
            });
            await context.SaveChangesAsync(CancellationToken);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        var studio = new TemporaryFolder();
        var handler = new IdTokenRewriteHandler(editIdToken: IdToken(identityProvider));
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", studio.Path);
            builder.ConfigureTestServices(services =>
                services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName("axis"), options => options.BackchannelHttpHandler = handler));
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _address,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        return new Setup(factory, client, handler, [applications, studio], authority, tenant.ConnectionString);
    }

    private sealed class Setup(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        IdTokenRewriteHandler handler,
        TemporaryFolder[] folders,
        Uri authority,
        string tenantDatabase) : IAsyncDisposable
    {
        public HttpClient Client => client;

        /// <summary>Signs in as <c>requester</c> at Keycloak and sends the callback to the server.</summary>
        public async Task<HttpResponseMessage> CallbackAsync()
        {
            var callback = await KeycloakSignIn.LogInAsync(client, authority, "requester", _address);
            return await client.GetAsync(callback.PathAndQuery, CancellationToken);
        }

        public async Task<long> CountAsync(string from) =>
            (long)(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"SELECT count(*) FROM {from}", CancellationToken))!;

        public async Task<string?> RefusalReasonAsync()
        {
            var details = (string)(await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase,
                "SELECT details::text FROM axis.audit_records WHERE action = 'session.signInRefused'",
                CancellationToken))!;
            using var document = JsonDocument.Parse(details);
            return document.RootElement.GetProperty("reason").GetString();
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
            handler.Dispose();
            foreach (var folder in folders)
            {
                folder.Dispose();
            }
        }
    }
}
