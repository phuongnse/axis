using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// A lease of a tenant's quota is a row of <c>platform.tenant_leases</c> that every host shares, see
/// <c>docs/reference/tenant-quotas.md#work-at-once</c>. It lasts at most 60 seconds past its last renewal, is renewed while
/// held and is deleted when released.
/// </summary>
public sealed class TenantLeasesTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_lease_is_renewed_while_held_never_outlives_sixty_seconds_past_its_renewal_and_is_deleted_on_release()
    {
        var platform = await PreparePlatformAsync();
        await using var dataSource = NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform));
        var leases = new TenantLeases(dataSource, NullLogger<TenantLeases>.Instance, TimeSpan.FromMilliseconds(200));

        var first = await leases.TryTakeAsync("a", QuotaKeys.ConcurrentQueries, 1, CancellationToken);
        Assert.NotNull(first);
        var taken = await ExpiresAtAsync(platform);
        Assert.InRange(await SecondsLeftAsync(platform), 0, 60);

        // The renewal moves the expiry on, and it never goes past sixty seconds from the database clock.
        await WaitUntilAsync(async () => await ExpiresAtAsync(platform) > taken, "The lease was not renewed.");
        Assert.InRange(await SecondsLeftAsync(platform), 0, 60);

        // The slot is full while the lease is held, and free again after it is released.
        Assert.Null(await leases.TryTakeAsync("a", QuotaKeys.ConcurrentQueries, 1, CancellationToken));
        await first.DisposeAsync();
        Assert.Equal(0L, await CountAsync(platform));
        var second = await leases.TryTakeAsync("a", QuotaKeys.ConcurrentQueries, 1, CancellationToken);
        Assert.NotNull(second);
        await second.DisposeAsync();
        Assert.Equal(0L, await CountAsync(platform));
    }

    [Fact]
    public async Task A_lease_that_stopped_renewing_expires_and_frees_its_slot_and_other_tenants_and_quotas_do_not_share_it()
    {
        var platform = await PreparePlatformAsync();
        await using var dataSource = NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform));
        var leases = new TenantLeases(dataSource, NullLogger<TenantLeases>.Instance);
        await PostgreSqlServer.AdminScalarAsync(
            platform,
            "INSERT INTO platform.tenant_leases (lease_id, tenant_id, quota, holder, expires_at) VALUES (gen_random_uuid(), 'a', 'concurrentQueries', 'dead', now() + interval '2 seconds')",
            CancellationToken);

        Assert.Null(await leases.TryTakeAsync("a", QuotaKeys.ConcurrentQueries, 1, CancellationToken));
        await using (var other = await leases.TryTakeAsync("b", QuotaKeys.ConcurrentQueries, 1, CancellationToken))
        {
            Assert.NotNull(other);
        }

        await WaitUntilAsync(
            async () =>
            {
                var lease = await leases.TryTakeAsync("a", QuotaKeys.ConcurrentQueries, 1, CancellationToken);
                if (lease is null)
                {
                    return false;
                }

                await lease.DisposeAsync();
                return true;
            },
            "The expired lease still held the slot.");

        // The take deleted the expired row.
        Assert.Equal(0L, await CountAsync(platform));
    }

    // A platform database with the tables, the tenants a and b and the reader role's grants.
    private async Task<string> PreparePlatformAsync()
    {
        var platform = await database.CreateDatabaseAsync("leases_platform", CancellationToken);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        foreach (var id in new[] { "a", "b" })
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

        await context.SaveChangesAsync(CancellationToken);
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        return platform;
    }

    private static async Task<long> CountAsync(string platform) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_leases", CancellationToken))!;

    private static async Task<DateTime> ExpiresAtAsync(string platform) =>
        (DateTime)(await PostgreSqlServer.AdminScalarAsync(platform, "SELECT expires_at FROM platform.tenant_leases", CancellationToken))!;

    // How far the single lease's expiry is past the database clock.
    private static async Task<double> SecondsLeftAsync(string platform) =>
        (double)(await PostgreSqlServer.AdminScalarAsync(
            platform, "SELECT extract(epoch FROM expires_at - clock_timestamp())::float8 FROM platform.tenant_leases", CancellationToken))!;
}
