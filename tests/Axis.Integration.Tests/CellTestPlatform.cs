using Axis.Processes.Storage;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The platform database that the tests of a cell's console host share. It holds three tenants and a preview tenant:
/// <c>a</c> is active and served at <c>localhost</c> from a migrated database, <c>b</c> is active, and <c>c</c> is suspended.
/// Each tenant signs its people in at the development <c>axis</c> realm.
/// </summary>
internal static class CellTestPlatform
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    /// <summary>Creates and migrates the platform database and tenant a's database, adds the tenants and applies the reader role's grants. Returns the platform connection string.</summary>
    public static async Task<string> CreateAsync(PostgreSqlFixture database, Uri authority, CancellationToken cancellationToken)
    {
        var platform = await database.CreateDatabaseAsync("cell_api_platform", cancellationToken);
        var tenantDatabase = await database.CreateDatabaseAsync("cell_api_a", cancellationToken);
        await using (var connection = new NpgsqlConnection(tenantDatabase))
        {
            await connection.OpenAsync(cancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(cancellationToken);
            await data.Database.MigrateAsync(cancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, cancellationToken);
        var served = new NpgsqlConnectionStringBuilder(tenantDatabase);

        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options))
        {
            await context.Database.MigrateAsync(cancellationToken);

            // Tenant b has no recorded schema version, so the server withholds it and opens no database for it.
            context.Tenants.AddRange(
                Tenant("a", "active", TenantSchemaVersion.Current, served, authority),
                Tenant("b", "active", string.Empty, served, authority),
                Tenant("c", "suspended", string.Empty, served, authority),
                Tenant("a-preview", "suspended", string.Empty, served, authority, previewOf: "a"));
            context.TenantHosts.AddRange(
                new TenantHostRow { TenantId = "a", Host = "localhost" },
                new TenantHostRow { TenantId = "b", Host = "b2.vn.example.test" },
                new TenantHostRow { TenantId = "b", Host = "b.vn.example.test" },
                new TenantHostRow { TenantId = "c", Host = "c.vn.example.test" },
                new TenantHostRow { TenantId = "a-preview", Host = "a-preview.vn.example.test" });
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(cancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, cancellationToken);
        }

        return platform;
    }

    /// <summary>
    /// Adds a tenant of the given status whose id is the name of a realm that <c>TenantRealm</c> made at <paramref name="identityProvider"/>,
    /// with its own migrated database and the users client secret of that realm. Returns the tenant database's connection string.
    /// </summary>
    public static async Task<string> AddRealmTenantAsync(
        PostgreSqlFixture database,
        string platform,
        string id,
        string status,
        Uri identityProvider,
        string usersSecret,
        CancellationToken cancellationToken)
    {
        var tenantDatabase = await database.CreateDatabaseAsync("cell_api_realm", cancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(tenantDatabase, cancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, cancellationToken);

        var row = Tenant(id, status, TenantSchemaVersion.Current, new NpgsqlConnectionStringBuilder(tenantDatabase), new Uri(identityProvider, $"realms/{id}"));
        row.RealmUsersClientSecret = _cipher.Encrypt(id, TenantSecretFields.RealmUsersClientSecret, usersSecret);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        context.Tenants.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        return tenantDatabase;
    }

    private static TenantRow Tenant(
        string id,
        string status,
        string schemaVersion,
        NpgsqlConnectionStringBuilder databaseAddress,
        Uri authority,
        string? previewOf = null) =>
        new()
        {
            Id = id,
            Status = status,
            SchemaVersion = schemaVersion,
            PreviewOf = previewOf,
            DatabaseHost = databaseAddress.Host!,
            DatabasePort = databaseAddress.Port,
            DatabaseName = databaseAddress.Database!,
            RuntimeUser = PostgreSqlServer.RuntimeRole,
            SchemaUser = PostgreSqlServer.SchemaRole,
            RuntimePassword = _cipher.Encrypt(id, TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
            SchemaPassword = _cipher.Encrypt(id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
            OidcAuthority = authority.ToString(),
            OidcClientId = KeycloakFixture.ClientId,
            OidcClientSecret = _cipher.Encrypt(id, TenantSecretFields.OidcClientSecret, KeycloakFixture.ClientSecret),
        };
}
