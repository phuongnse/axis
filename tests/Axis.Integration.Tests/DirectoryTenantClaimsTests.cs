using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// A directory tenant names the claims of its ID token in its directory entry. The email and locale claim names come
/// from <c>claim_email</c> and <c>claim_locale</c>, and a tenant that names none uses the defaults.
/// </summary>
public sealed class DirectoryTenantClaimsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_directory_tenants_email_and_locale_claim_names_are_read_from_its_entry()
    {
        var platform = await SeedPlatformAsync(
            "claims_named",
            new TenantRow { Id = "named", ClaimEmail = "mail", ClaimLocale = "lang" },
            new TenantRow { Id = "blank", ClaimEmail = " ", ClaimLocale = "" },
            new TenantRow { Id = "plain" });

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);

        var named = catalog.Find("named")!.Oidc!;
        Assert.Equal("mail", named.EmailClaim);
        Assert.Equal("lang", named.LocaleClaim);
        var settings = TenantOidcSettings.TryCreate(catalog.Find("named")!, []);
        Assert.Equal("mail", settings?.Claims.Email);
        Assert.Equal("lang", settings?.Claims.Locale);

        foreach (var id in new[] { "blank", "plain" })
        {
            var oidc = catalog.Find(id)!.Oidc!;
            Assert.Null(oidc.EmailClaim);
            Assert.Null(oidc.LocaleClaim);
            var defaults = TenantOidcSettings.TryCreate(catalog.Find(id)!, []);
            Assert.Equal("email", defaults?.Claims.Email);
            Assert.Equal("locale", defaults?.Claims.Locale);
        }
    }

    private async Task<string> SeedPlatformAsync(string name, params TenantRow[] tenants)
    {
        var platform = await database.CreateDatabaseAsync(name, CancellationToken);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        foreach (var tenant in tenants)
        {
            tenant.Status = "active";
            tenant.SchemaVersion = TenantSchemaVersion.Current;
            tenant.DatabaseHost = "127.0.0.1";
            tenant.DatabasePort = 5432;
            tenant.DatabaseName = $"axis_{tenant.Id}";
            tenant.RuntimeUser = PostgreSqlServer.RuntimeRole;
            tenant.SchemaUser = PostgreSqlServer.SchemaRole;
            tenant.RuntimePassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword);
            tenant.SchemaPassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword);
            tenant.OidcAuthority = "https://127.0.0.1:1/realms/axis";
            tenant.OidcClientId = "axis";
            tenant.OidcClientSecret = _cipher.Encrypt(tenant.Id, TenantSecretFields.OidcClientSecret, "test");
            context.Tenants.Add(tenant);
            context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = $"{tenant.Id}.example.test" });
        }

        await context.SaveChangesAsync(CancellationToken);
        await using var connection = new Npgsql.NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        return platform;
    }
}
