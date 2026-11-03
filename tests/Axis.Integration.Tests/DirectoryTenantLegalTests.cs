using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// A directory tenant's privacy and terms links, and its sites' own, are read into the catalog from the directory. A
/// preview tenant takes its main tenant's, as it does for sign-in.
/// </summary>
public sealed class DirectoryTenantLegalTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_directory_tenants_links_and_site_overrides_are_read_and_a_preview_tenant_takes_its_main_tenants()
    {
        var platform = await SeedPlatformAsync(
            "legal_links",
            [
                new TenantRow
                {
                    Id = "main",
                    PrivacyUrl = "https://main.example.test/privacy",
                    TermsUrl = "https://main.example.test/terms",
                    TermsVersion = "2026-11-01",
                },
                new TenantRow { Id = "plain" },
                new TenantRow { Id = "preview", PreviewOf = "main" },
            ],
            new TenantSiteLegalRow { TenantId = "main", SitePath = "shop", PrivacyUrl = "https://main.example.test/shop-privacy" });

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);

        var main = catalog.Find("main")!.Legal!;
        Assert.Equal("https://main.example.test/privacy", main.PrivacyUrl);
        Assert.Equal("https://main.example.test/terms", main.TermsUrl);
        Assert.Equal("2026-11-01", main.TermsVersion);
        Assert.Equal(new SiteLegal("https://main.example.test/shop-privacy", null), main.ForSite("Shop"));
        Assert.Equal(new SiteLegal("https://main.example.test/privacy", "https://main.example.test/terms"), main.ForSite("other"));
        Assert.Null(catalog.Find("plain")!.Legal);

        var preview = catalog.Find("preview")!.Legal!;
        Assert.Equal(main.PrivacyUrl, preview.PrivacyUrl);
        Assert.Equal(main.TermsUrl, preview.TermsUrl);
        Assert.Equal(main.TermsVersion, preview.TermsVersion);
        Assert.Equal(main.ForSite("shop"), preview.ForSite("shop"));
    }

    private async Task<string> SeedPlatformAsync(string name, TenantRow[] tenants, params TenantSiteLegalRow[] siteLinks)
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

        context.TenantSiteLegalLinks.AddRange(siteLinks);
        await context.SaveChangesAsync(CancellationToken);
        await using var connection = new Npgsql.NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        return platform;
    }
}
