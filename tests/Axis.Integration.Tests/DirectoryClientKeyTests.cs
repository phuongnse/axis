using System.Security.Cryptography;
using System.Text;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A directory tenant can be set to the <c>fapi2</c> sign-in profile and holds up to two client keys, each stored
/// encrypted with its key id. A preview tenant reads the profile and the keys of its main tenant.
/// </summary>
public sealed class DirectoryClientKeyTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fapi2_tenant_reads_its_profile_and_both_keys_newest_first_and_each_stored_key_is_bound_to_its_tenant_and_field()
    {
        var current = NewPem();
        var previous = NewPem();
        var platform = await database.CreateDatabaseAsync("client_key_platform", CancellationToken);
        var tenantDatabase = await database.CreateDatabaseAsync("client_key_tenant", CancellationToken);
        await SeedAsync(platform, tenantDatabase, Fapi2Row("a", "key-2", current, "key-1", previous));

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);

        var oidc = catalog.Find("a")!.Oidc!;
        Assert.Equal("fapi2", oidc.Profile);
        Assert.Equal(["key-2", "key-1"], oidc.ClientKeys!.Select(key => key.KeyId));
        Assert.Equal([current, previous], oidc.ClientKeys!.Select(key => key.PrivateKey.Reveal()));
        foreach (var (column, pem) in new[] { ("oidc_client_key", current), ("oidc_previous_client_key", previous) })
        {
            var stored = (byte[])(await PostgreSqlServer.AdminScalarAsync(platform, $"SELECT {column} FROM platform.tenants WHERE id = 'a'", CancellationToken))!;
            Assert.False(stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(pem)) >= 0, "The stored key holds the plain PEM.");
            Assert.Equal(pem, _cipher.Decrypt("a", TenantSecretFields.OidcClientKey, stored).Reveal());
            Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("b", TenantSecretFields.OidcClientKey, stored));
            Assert.Throws<InvalidOperationException>(() => _cipher.Decrypt("a", TenantSecretFields.OidcClientSecret, stored));
        }
    }

    [Fact]
    public async Task A_preview_tenant_of_a_fapi2_tenant_reads_the_profile_and_the_keys_of_its_main_tenant()
    {
        var current = NewPem();
        var previous = NewPem();
        var platform = await database.CreateDatabaseAsync("client_key_platform", CancellationToken);
        var tenantDatabase = await database.CreateDatabaseAsync("client_key_tenant", CancellationToken);
        var preview = Fapi2Row("m-preview", null, null, null, null);
        preview.PreviewOf = "m";
        preview.OidcProfile = "standard";
        preview.OidcAuthority = "";
        preview.OidcClientId = "";
        preview.OidcClientSecret = [];
        await SeedAsync(platform, tenantDatabase, Fapi2Row("m", "key-2", current, "key-1", previous), preview);

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);

        var oidc = catalog.Find("m-preview")!.Oidc!;
        Assert.Equal("fapi2", oidc.Profile);
        Assert.Equal(["key-2", "key-1"], oidc.ClientKeys!.Select(key => key.KeyId));
        Assert.Equal([current, previous], oidc.ClientKeys!.Select(key => key.PrivateKey.Reveal()));
        Assert.Null(oidc.EndpointAudience);
    }

    [Fact]
    public async Task A_change_of_profile_or_key_counts_as_a_sign_in_change()
    {
        var platform = await database.CreateDatabaseAsync("client_key_platform", CancellationToken);
        var tenantDatabase = await database.CreateDatabaseAsync("client_key_tenant", CancellationToken);
        await SeedAsync(platform, tenantDatabase, Fapi2Row("a", "key-1", NewPem(), null, null));
        var before = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);
        Assert.Empty(before.OidcChanges(before));

        await PostgreSqlServer.AdminScalarAsync(platform, "UPDATE platform.tenants SET oidc_profile = 'standard' WHERE id = 'a'", CancellationToken);
        var profileChanged = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);
        Assert.Equal(["a"], profileChanged.OidcChanges(before));

        await PostgreSqlServer.AdminScalarAsync(platform, "UPDATE platform.tenants SET oidc_client_key_id = 'key-2' WHERE id = 'a'", CancellationToken);
        var keyChanged = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);
        Assert.Equal(["a"], keyChanged.OidcChanges(profileChanged));
    }

    [Fact]
    public async Task The_directory_refuses_an_unknown_profile_and_a_previous_key_without_a_current_key()
    {
        var platform = await database.CreateDatabaseAsync("client_key_platform", CancellationToken);
        var tenantDatabase = await database.CreateDatabaseAsync("client_key_tenant", CancellationToken);
        await SeedAsync(platform, tenantDatabase, Fapi2Row("a", "key-1", NewPem(), null, null));

        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgreSqlServer.AdminScalarAsync(platform, "UPDATE platform.tenants SET oidc_profile = 'other' WHERE id = 'a'", CancellationToken));
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgreSqlServer.AdminScalarAsync(platform, "UPDATE platform.tenants SET oidc_client_key = NULL, oidc_client_key_id = NULL, oidc_previous_client_key = '\\x00', oidc_previous_client_key_id = 'old' WHERE id = 'a'", CancellationToken));
    }

    // The test keys are generated at run time, so no key is committed.
    private static string NewPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static TenantRow Fapi2Row(string id, string? keyId, string? pem, string? previousKeyId, string? previousPem) => new()
    {
        Id = id,
        Status = "active",
        SchemaVersion = TenantSchemaVersion.Current,
        OidcProfile = "fapi2",
        OidcAuthority = "https://127.0.0.1:1/realms/axis",
        OidcClientId = "axis",
        OidcClientSecret = [],
        OidcClientKeyId = keyId,
        OidcClientKey = pem is null ? null : _cipher.Encrypt(id, TenantSecretFields.OidcClientKey, pem),
        OidcPreviousClientKeyId = previousKeyId,
        OidcPreviousClientKey = previousPem is null ? null : _cipher.Encrypt(id, TenantSecretFields.OidcClientKey, previousPem),
    };

    private static async Task SeedAsync(string platform, string tenantDatabase, params TenantRow[] rows)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(tenantDatabase);
        foreach (var row in rows)
        {
            row.DatabaseHost = builder.Host!;
            row.DatabasePort = builder.Port;
            row.DatabaseName = builder.Database!;
            row.RuntimeUser = PostgreSqlServer.RuntimeRole;
            row.SchemaUser = PostgreSqlServer.SchemaRole;
            row.RuntimePassword = _cipher.Encrypt(row.Id, TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword);
            row.SchemaPassword = _cipher.Encrypt(row.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword);
            context.Tenants.Add(row);
            context.TenantHosts.Add(new TenantHostRow { TenantId = row.Id, Host = $"{row.Id}.example.test" });
        }

        await context.SaveChangesAsync(CancellationToken);

        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
    }
}
