using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The platform grant step takes the platform grant lock, so the grant steps of servers that start together run one
/// after the other. The test holds the lock on its own connection and checks that a grant queues on it until the lock is
/// released.
/// </summary>
public sealed class PlatformRoleGrantsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_platform_grant_waits_while_another_holds_the_grant_lock_and_then_succeeds()
    {
        var platform = await database.CreateDatabaseAsync("platformgrantlock", CancellationToken);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await using var holder = new NpgsqlConnection(platform);
        await holder.OpenAsync(CancellationToken);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken);
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({PlatformRoleGrants.LockKey})", holder, transaction))
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        var grant = PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        await WaitForWaiterAsync(platform);
        Assert.False(grant.IsCompleted);

        await transaction.CommitAsync(CancellationToken);
        await grant;
    }

    private static Task WaitForWaiterAsync(string connectionString) =>
        WaitUntilAsync(async () => await WaitingCountAsync(connectionString) >= 1, "The grant did not queue on the lock in time.");

    private static async Task<long> WaitingCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory'
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
              AND NOT granted
            """,
            connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
