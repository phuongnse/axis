using Axis.Data.Storage;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The grant step takes the grant lock, so the grant steps of servers that start together run one after the other.
/// The test holds the lock on its own connection and checks that a grant queues on it until the lock is released.
/// </summary>
public sealed class RuntimeRoleGrantsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grant_waits_while_another_holds_the_grant_lock_and_then_succeeds()
    {
        var schemaConnection = await database.CreateDatabaseAsync("grantlock", CancellationToken);
        await using var holder = new NpgsqlConnection(schemaConnection);
        await holder.OpenAsync(CancellationToken);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken);
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({RuntimeRoleGrants.LockKey})", holder, transaction))
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var grant = PostgreSqlServer.GrantRuntimeRoleAsync(schemaConnection, CancellationToken);
        await WaitForWaiterAsync(schemaConnection);
        Assert.False(grant.IsCompleted);

        await transaction.CommitAsync(CancellationToken);
        await grant;
    }

    // Waits until the grant queues on the lock. The deadline keeps a grant that never waits from hanging the test.
    private static async Task WaitForWaiterAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await WaitingCountAsync(connectionString) < 1)
        {
            Assert.True(DateTime.UtcNow < deadline, "The grant did not queue on the lock in time.");
            await Task.Delay(50, CancellationToken);
        }
    }

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
