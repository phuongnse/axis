using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A platform database for the tests of <c>platform.tenant_leases</c>, which has a foreign key to <c>platform.tenants</c>.
/// The caller says how the database is created, so a test class that owns a <see cref="PostgreSqlFixture"/> has it dropped.
/// </summary>
internal static class TestLeasePlatform
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    /// <summary>A platform database with the tables, the given tenants and the reader role's grants. Returns its connection string.</summary>
    public static async Task<string> CreateAsync(
        Func<string, CancellationToken, Task<string>> createDatabase,
        string prefix,
        CancellationToken cancellationToken,
        params string[] tenantIds)
    {
        var platform = await createDatabase(prefix, cancellationToken);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(cancellationToken);
        foreach (var id in tenantIds)
        {
            context.Tenants.Add(new TenantRow
            {
                Id = id,
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = "127.0.0.1",
                DatabasePort = 5432,
                DatabaseName = $"tenant_{id}",
                RuntimeUser = "runtime",
                SchemaUser = "schema",
                RuntimePassword = _cipher.Encrypt(id, TenantSecretFields.RuntimePassword, "x"),
                SchemaPassword = _cipher.Encrypt(id, TenantSecretFields.SchemaPassword, "x"),
                OidcAuthority = "https://127.0.0.1:1/realms/axis",
                OidcClientId = "axis",
                OidcClientSecret = _cipher.Encrypt(id, TenantSecretFields.OidcClientSecret, "x"),
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(cancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, cancellationToken);
        return platform;
    }
}
