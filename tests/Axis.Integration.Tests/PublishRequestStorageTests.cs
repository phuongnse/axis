using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PublishRequestStorageTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Runtime_role_inserts_a_queued_request_and_reads_it_back_with_no_claims()
    {
        var schema = await MigratedDatabaseAsync();
        var runtime = PostgreSqlServer.RuntimeConnectionString(schema);
        var id = await InsertAsync(runtime);

        Assert.Equal("queued", await ScalarAsync(runtime, $"SELECT status FROM axis.publish_requests WHERE id = '{id}'"));
        Assert.Equal(0, await ScalarAsync(runtime, $"SELECT claims FROM axis.publish_requests WHERE id = '{id}'"));
        Assert.Equal(true, await ScalarAsync(runtime, $"SELECT requested_at IS NOT NULL FROM axis.publish_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task Runtime_role_cannot_update_delete_or_truncate_a_request()
    {
        var schema = await MigratedDatabaseAsync();
        var runtime = PostgreSqlServer.RuntimeConnectionString(schema);
        await InsertAsync(runtime);

        foreach (var sql in new[]
        {
            "UPDATE axis.publish_requests SET status = 'running'",
            "DELETE FROM axis.publish_requests",
            "TRUNCATE axis.publish_requests",
        })
        {
            await AssertSqlStateAsync(runtime, sql, PostgresErrorCodes.InsufficientPrivilege);
        }

        Assert.Equal(1L, await ScalarAsync(schema, "SELECT count(*) FROM axis.publish_requests"));
    }

    [Fact]
    public async Task Schema_role_changes_the_status_and_the_lease_of_a_request()
    {
        var schema = await MigratedDatabaseAsync();
        var id = await InsertAsync(PostgreSqlServer.RuntimeConnectionString(schema));

        await ScalarAsync(
            schema,
            $"""
            UPDATE axis.publish_requests
            SET status = 'running', claimed_by = 'p1', lease_until = now() + interval '1 minute', claims = claims + 1, started_at = now()
            WHERE id = '{id}'
            """);

        Assert.Equal("running", await ScalarAsync(schema, $"SELECT status FROM axis.publish_requests WHERE id = '{id}'"));
        Assert.Equal("p1", await ScalarAsync(schema, $"SELECT claimed_by FROM axis.publish_requests WHERE id = '{id}'"));
        Assert.Equal(1, await ScalarAsync(schema, $"SELECT claims FROM axis.publish_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task Grant_step_takes_wider_rights_on_requests_away_from_the_runtime_role()
    {
        var schema = await MigratedDatabaseAsync();
        var runtime = PostgreSqlServer.RuntimeConnectionString(schema);
        await ScalarAsync(schema, $"GRANT ALL ON axis.publish_requests TO {PostgreSqlServer.RuntimeRole}");
        await ScalarAsync(runtime, "UPDATE axis.publish_requests SET status = 'queued'");

        await PostgreSqlServer.GrantRuntimeRoleAsync(schema, CancellationToken);

        await AssertSqlStateAsync(runtime, "UPDATE axis.publish_requests SET status = 'running'", PostgresErrorCodes.InsufficientPrivilege);
        await InsertAsync(runtime);
    }

    [Fact]
    public async Task Migrated_database_has_the_claim_index()
    {
        var schema = await MigratedDatabaseAsync();

        var definition = (string?)await ScalarAsync(
            schema,
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'axis' AND indexname = 'ix_publish_requests_application_id_status_requested_at_id'
            """);

        Assert.NotNull(definition);
        Assert.EndsWith("(application_id, status, requested_at, id)", definition, StringComparison.Ordinal);
    }

    private async Task<string> MigratedDatabaseAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("publish", CancellationToken);
        await using (var context = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }

    private static async Task<Guid> InsertAsync(string connectionString)
    {
        var id = Guid.NewGuid();
        await ScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.publish_requests (id, application_id, kind, status, snapshot, requested_by)
            VALUES ('{id}', '{Guid.NewGuid()}', 'publish', 'queued', '[]'::jsonb, 'hoa')
            """);
        return id;
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(CancellationToken);
    }

    /// <summary>Runs <paramref name="sql"/> and checks that PostgreSQL refuses it with <paramref name="sqlState"/>.</summary>
    private static async Task AssertSqlStateAsync(string connectionString, string sql, string sqlState)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(connectionString, sql));
        Assert.True(exception.SqlState == sqlState, $"{sql} gave {exception.SqlState}, expected {sqlState}: {exception.Message}");
    }
}
