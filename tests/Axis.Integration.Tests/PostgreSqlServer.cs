using System.Text.RegularExpressions;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.PostgreSqlServer))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real PostgreSQL container shared by the whole run. It starts the first time a test needs it,
/// so a run that needs no PostgreSQL never starts one. Fixtures create <see cref="PostgreSqlFixture"/>
/// with <c>new</c>, so xUnit cannot inject a shared instance into them, and the container is held in
/// static state. This assembly fixture only removes the container when the run ends.
/// </summary>
public sealed partial class PostgreSqlServer : IAsyncDisposable
{
    public const string Image = "postgres:18-alpine";

    /// <summary>
    /// xUnit runs about one test class per CPU core in parallel, and one class can hold about 30
    /// connections: the server pools, the worker, data sources and the 20 concurrent requests in
    /// <see cref="RecordSequenceEndpointTests"/>. The default of 100 is too low for that.
    /// </summary>
    public const int MaxConnections = 500;

    // The start takes no test's cancellation token, so cancelling one test does not fail the others.
    private static readonly Lazy<Task<PostgreSqlContainer>> _container = new(StartContainerAsync);

    /// <summary>
    /// Creates a new database named <c>&lt;prefix&gt;_&lt;guid&gt;</c>, unique in the run, and returns
    /// a connection string to it. The prefix is at most 30 lowercase letters, digits and underscores,
    /// so the name stays within PostgreSQL's 63-character limit.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (!PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException(
                "A database prefix starts with a lowercase letter and has at most 30 lowercase letters, digits and underscores.",
                nameof(prefix));
        }

        var name = $"{prefix}_{Guid.NewGuid():N}";
        var admin = await AdminConnectionStringAsync();
        await ExecuteAsync(admin, $"""CREATE DATABASE "{name}" """, cancellationToken);
        return new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
    }

    /// <summary>Drops the database and ends every session still connected to it.</summary>
    public static async Task DropDatabaseAsync(string name, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(await AdminConnectionStringAsync(), $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    /// <summary>The connection string to the maintenance database, which creates and drops the others.</summary>
    public static async Task<string> AdminConnectionStringAsync() => (await _container.Value).GetConnectionString();

    // CREATE DATABASE and DROP DATABASE cannot run in a transaction block, so each is its own command.
    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartContainerAsync()
    {
        var container = new PostgreSqlBuilder(Image)
            .WithCommand("-c", $"max_connections={MaxConnections}")
            .Build();
        try
        {
            await container.StartAsync();
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,29}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
