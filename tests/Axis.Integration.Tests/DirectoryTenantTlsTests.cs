using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A directory tenant's connection takes the TLS settings of the platform connection string. The test PostgreSQL has no
/// TLS, so the settings reach the catalog apart from the string that reads the directory.
/// </summary>
public sealed class DirectoryTenantTlsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tenants_connection_string_carries_the_ssl_mode_and_root_certificate_of_the_platform_settings()
    {
        var platform = await SeedPlatformAsync("tls_copied");
        var tls = TenantDatabaseTls.From("Host=x;SSL Mode=VerifyFull;Root Certificate=/certs/root.pem");

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), tls, _cipher, CancellationToken);

        var builder = new NpgsqlConnectionStringBuilder(catalog.Find("one")!.ConnectionString.Reveal());
        Assert.Equal(SslMode.VerifyFull, builder.SslMode);
        Assert.Equal("/certs/root.pem", builder.RootCertificate);
    }

    [Fact]
    public async Task Under_ssl_mode_require_a_tenant_connection_to_a_server_without_tls_fails()
    {
        var platform = await SeedPlatformAsync("tls_required");
        var tls = TenantDatabaseTls.From("Host=x;SSL Mode=Require");

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), tls, _cipher, CancellationToken);

        var connectionString = new NpgsqlConnectionStringBuilder(catalog.Find("one")!.ConnectionString.Reveal()) { Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => connection.OpenAsync(CancellationToken));
    }

    [Fact]
    public async Task The_public_load_copies_the_ssl_mode_of_the_platform_connection_string()
    {
        var platform = await SeedPlatformAsync("tls_public");

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform) + ";SSL Mode=Disable", _cipher, CancellationToken);

        Assert.Equal(SslMode.Disable, new NpgsqlConnectionStringBuilder(catalog.Find("one")!.ConnectionString.Reveal()).SslMode);
    }

    private async Task<string> SeedPlatformAsync(string name)
    {
        var platform = await database.CreateDatabaseAsync(name, CancellationToken);
        var server = new NpgsqlConnectionStringBuilder(platform);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        var tenant = new TenantRow
        {
            Id = "one",
            Status = "active",
            SchemaVersion = TenantSchemaVersion.Current,
            DatabaseHost = server.Host!,
            DatabasePort = server.Port,
            DatabaseName = server.Database!,
            RuntimeUser = PostgreSqlServer.RuntimeRole,
            SchemaUser = PostgreSqlServer.SchemaRole,
            RuntimePassword = _cipher.Encrypt("one", TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
            SchemaPassword = _cipher.Encrypt("one", TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
            OidcAuthority = "https://127.0.0.1:1/realms/axis",
            OidcClientId = "axis",
            OidcClientSecret = _cipher.Encrypt("one", TenantSecretFields.OidcClientSecret, "test"),
        };
        context.Tenants.Add(tenant);
        context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = "one.example.test" });
        await context.SaveChangesAsync(CancellationToken);

        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        return platform;
    }
}
