using System.Globalization;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PostgreSqlFixtureTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_fixture_works_in_its_own_database()
    {
        await using var first = new PostgreSqlFixture();
        await using var second = new PostgreSqlFixture();
        await first.InitializeAsync();
        await second.InitializeAsync();

        Assert.NotEqual(await ScalarAsync(first.ConnectionString, "SELECT current_database()"), await ScalarAsync(second.ConnectionString, "SELECT current_database()"));

        await ScalarAsync(first.ConnectionString, "CREATE TABLE isolation_probe (id int)");

        Assert.NotNull(await ScalarAsync(first.ConnectionString, "SELECT to_regclass('isolation_probe')::text"));
        Assert.Null(await ScalarAsync(second.ConnectionString, "SELECT to_regclass('isolation_probe')::text"));
    }

    [Fact]
    public async Task Shared_server_allows_enough_connections_for_parallel_classes()
    {
        await using var database = new PostgreSqlFixture();
        await database.InitializeAsync();

        Assert.Equal(
            PostgreSqlServer.MaxConnections.ToString(CultureInfo.InvariantCulture),
            await ScalarAsync(database.ConnectionString, "SHOW max_connections"));
    }

    [Fact]
    public async Task Database_names_are_unique_and_within_the_postgresql_limit()
    {
        await using var database = new PostgreSqlFixture();
        var prefix = new string('p', 30);

        var names = new[]
        {
            new NpgsqlConnectionStringBuilder(await database.CreateDatabaseAsync(prefix, CancellationToken)).Database!,
            new NpgsqlConnectionStringBuilder(await database.CreateDatabaseAsync(prefix, CancellationToken)).Database!,
        };

        Assert.NotEqual(names[0], names[1]);
        Assert.All(names, name => Assert.InRange(name.Length, 1, 63));
        await Assert.ThrowsAsync<ArgumentException>(() => database.CreateDatabaseAsync(prefix + "p", CancellationToken));
    }

    /// <summary>Runs the statement and returns its scalar, with DBNull as null.</summary>
    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        var value = await command.ExecuteScalarAsync(CancellationToken);
        return value is DBNull ? null : value;
    }
}
