using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
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
/// The platform pages of a directory tenant honour its stored platform sign-in. The sign-in callback refuses a provider
/// that the platform sign-in does not accept, and an account sign-in when accounts are off. For an accepted provider it
/// turns the values of the provider's roles claim into platform roles. The tests rewrite the ID token that Keycloak
/// returns, so it carries the <c>identity_provider</c> claim and the provider's roles attribute as the realm sync gives them.
/// </summary>
public sealed class PlatformSignInTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string Company = "oidc-companyissuer";
    private const string RolesClaim = "axis_roles_groups";

    // The realm's client accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _address = new("http://localhost:5206");

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sign_in_through_a_provider_that_the_platform_sign_in_does_not_accept_is_refused_and_writes_no_session()
    {
        await using var setup = await StartAsync(accounts: false, Provider("company"), IdToken("oidc-other", "axis-authors"));

        var callback = await setup.CallbackAsync();

        AssertRefused(callback);
        Assert.Equal(0L, await setup.CountAsync("axis.sessions"));
        Assert.Equal(0L, await setup.CountAsync("axis.audit_records WHERE action = 'session.signedIn'"));
        Assert.Equal("providerNotAccepted", await setup.RefusalReasonAsync());
        using var me = await setup.Client.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task An_account_sign_in_is_refused_when_the_platform_sign_in_has_accounts_off()
    {
        await using var setup = await StartAsync(accounts: false, Provider("company"), IdToken(identityProvider: null));

        var callback = await setup.CallbackAsync();

        AssertRefused(callback);
        Assert.Equal(0L, await setup.CountAsync("axis.sessions"));
        Assert.Equal("providerNotAccepted", await setup.RefusalReasonAsync());
    }

    [Fact]
    public async Task An_account_sign_in_is_accepted_when_the_platform_sign_in_has_accounts_on_and_no_providers()
    {
        await using var setup = await StartAsync(accounts: true, providers: [], IdToken(identityProvider: null));

        var callback = await setup.CallbackAsync();

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal(1L, await setup.CountAsync("axis.sessions"));
        using var me = await setup.Client.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var apps = await setup.Client.GetAsync("/api/studio/apps", CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, apps.StatusCode);
    }

    [Fact]
    public async Task A_provider_sign_in_whose_roles_claim_holds_the_value_mapped_to_author_opens_studio()
    {
        await using var setup = await StartAsync(accounts: false, Provider("company"), IdToken(Company, "axis-authors"));

        var callback = await setup.CallbackAsync();

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        // The session keeps the roles of the tenant's roles claim and adds the mapped one.
        var roles = await setup.SessionRolesAsync();
        Assert.Contains("author", roles);
        Assert.Contains("requester", roles);
        using var apps = await setup.Client.GetAsync("/api/studio/apps", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, apps.StatusCode);
    }

    [Fact]
    public async Task A_provider_sign_in_without_the_mapped_value_is_denied_in_studio()
    {
        await using var setup = await StartAsync(accounts: false, Provider("company"), IdToken(Company, "staff"));

        var callback = await setup.CallbackAsync();

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var roles = await setup.SessionRolesAsync();
        Assert.DoesNotContain("author", roles);
        Assert.Contains("requester", roles);
        using var apps = await setup.Client.GetAsync("/api/studio/apps", CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, apps.StatusCode);
    }

    [Fact]
    public async Task The_directory_catalog_carries_the_platform_sign_in_and_a_preview_tenant_takes_its_main_tenants()
    {
        var keycloakAuthority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("platform_signin_catalog", CancellationToken);
        await using (var context = NewContext(platform))
        {
            await context.Database.MigrateAsync(CancellationToken);
            context.Tenants.Add(TenantRow("a", keycloakAuthority, accounts: false, previewOf: null));
            context.Tenants.Add(TenantRow("b", keycloakAuthority, accounts: false, previewOf: null));
            context.Tenants.Add(TenantRow("p", keycloakAuthority, accounts: true, previewOf: "a"));
            context.TenantPlatformProviders.Add(Provider("company"));
            await context.SaveChangesAsync(CancellationToken);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        var catalog = await DirectoryTenantCatalog.LoadAsync(
            PostgreSqlServer.RuntimeConnectionString(platform),
            _cipher,
            CancellationToken);

        var main = catalog.Find("a")!.PlatformSignIn!;
        Assert.False(main.Accounts);
        var company = Assert.Single(main.Providers);
        Assert.Equal(("company", Company, "groups"), (company.Name, company.Alias, company.RolesClaim));
        Assert.Equal(new Dictionary<string, string> { ["axis-authors"] = "author" }, company.RoleValues);
        Assert.Null(catalog.Find("b")!.PlatformSignIn);
        // A preview tenant has the main tenant's platform sign-in, not its own row's accounts.
        var inherited = Assert.Single(catalog.Find("p")!.PlatformSignIn!.Providers);
        Assert.Equal(("company", Company, "groups"), (inherited.Name, inherited.Alias, inherited.RolesClaim));
        Assert.Equal(company.RoleValues, inherited.RoleValues);
        Assert.False(catalog.Find("p")!.PlatformSignIn!.Accounts);
    }

    private static void AssertRefused(HttpResponseMessage callback)
    {
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal(TenantSignIn.FailedPath, callback.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            callback.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
    }

    // Keycloak signs the token, so the handler re-signs it with the claims the realm sync gives a brokered sign-in.
    private static Action<JsonObject> IdToken(string? identityProvider, params string[] roles) => claims =>
    {
        if (identityProvider is not null)
        {
            claims["identity_provider"] = identityProvider;
            claims[RolesClaim] = new JsonArray([.. roles.Select(role => JsonValue.Create(role))]);
        }
    };

    private static TenantPlatformProviderRow Provider(string name) =>
        new()
        {
            TenantId = "a",
            Name = name,
            Type = "oidc",
            IssuerBinding = "companyIssuer",
            ClientIdBinding = "companyClientId",
            ClientSecretBinding = "companySecret",
            RolesClaim = "groups",
            RoleValues = """{"axis-authors":"author"}""",
        };

    private static PlatformDbContext NewContext(string platform) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);

    private static TenantRow TenantRow(string id, Uri authority, bool accounts, string? previewOf) =>
        new()
        {
            Id = id,
            Status = "active",
            PreviewOf = previewOf,
            PlatformAccounts = accounts,
            SchemaVersion = TenantSchemaVersion.Current,
            DatabaseHost = "127.0.0.1",
            DatabasePort = 5432,
            DatabaseName = $"axis_{id}",
            RuntimeUser = PostgreSqlServer.RuntimeRole,
            SchemaUser = PostgreSqlServer.SchemaRole,
            RuntimePassword = _cipher.Encrypt(id, TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
            SchemaPassword = _cipher.Encrypt(id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
            OidcAuthority = authority.ToString(),
            OidcClientId = KeycloakFixture.ClientId,
            OidcClientSecret = _cipher.Encrypt(previewOf ?? id, TenantSecretFields.OidcClientSecret, KeycloakFixture.ClientSecret),
        };

    private async Task<Setup> StartAsync(bool accounts, TenantPlatformProviderRow provider, Action<JsonObject> editIdToken) =>
        await StartAsync(accounts, [provider], editIdToken);

    // Tenant a is the only tenant, at localhost, with the stored platform sign-in. Studio's author role is "author".
    private async Task<Setup> StartAsync(bool accounts, TenantPlatformProviderRow[] providers, Action<JsonObject> editIdToken)
    {
        var authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("platform_signin_platform", CancellationToken);
        var tenant = new NpgsqlConnectionStringBuilder(await database.CreateDatabaseAsync("platform_signin_a", CancellationToken));
        await using (var connection = new NpgsqlConnection(tenant.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(tenant.ConnectionString, CancellationToken);
        await using (var context = NewContext(platform))
        {
            await context.Database.MigrateAsync(CancellationToken);
            var row = TenantRow("a", authority, accounts, previewOf: null);
            row.DatabaseHost = tenant.Host!;
            row.DatabasePort = tenant.Port;
            row.DatabaseName = tenant.Database!;
            context.Tenants.Add(row);
            context.TenantHosts.Add(new TenantHostRow { TenantId = "a", Host = "localhost" });
            context.TenantPlatformProviders.AddRange(providers);
            await context.SaveChangesAsync(CancellationToken);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        var applications = new TemporaryFolder();
        var handler = new IdTokenRewriteHandler(editIdToken: editIdToken);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", applications.Path);
            builder.ConfigureTestServices(services =>
                services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName("a"), options => options.BackchannelHttpHandler = handler));
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _address,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        return new Setup(factory, client, handler, applications, authority, tenant.ConnectionString);
    }

    private sealed class Setup(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        IdTokenRewriteHandler handler,
        TemporaryFolder applications,
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

        public async Task<string[]> SessionRolesAsync() =>
            (string[])(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, "SELECT roles FROM axis.sessions", CancellationToken))!;

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
            handler.Dispose();
            applications.Dispose();
        }
    }
}
