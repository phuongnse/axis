using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One new database on the shared <see cref="PostgreSqlServer"/> for the tests in a class, so nothing
/// one class writes is visible to another. The fixture never starts a container of its own. It can
/// create more databases, each with a name unique in the run, and drops them all when it is disposed.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly Lock _lock = new();
    private readonly List<string> _databases = [];
    private string? _connectionString;

    /// <summary>The connection string to the class's own database.</summary>
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync() => _connectionString = await CreateDatabaseAsync("class");

    /// <summary>
    /// Creates another new database, named after the prefix and unique in the run, and returns a
    /// connection string to it. The fixture drops it when it is disposed.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgreSqlServer.CreateDatabaseAsync(prefix, cancellationToken);
        lock (_lock)
        {
            _databases.Add(new NpgsqlConnectionStringBuilder(connectionString).Database!);
        }

        return connectionString;
    }

    public async ValueTask DisposeAsync()
    {
        string[] databases;
        lock (_lock)
        {
            databases = [.. _databases];
            _databases.Clear();
        }

        // Dropping a database also ends its idle pooled connections, which would otherwise count
        // against max_connections while later classes run.
        foreach (var name in databases)
        {
            await PostgreSqlServer.DropDatabaseAsync(name);
        }
    }
}
