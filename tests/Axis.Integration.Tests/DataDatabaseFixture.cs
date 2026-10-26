using Axis.Configuration.Storage;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>A real PostgreSQL database with the configuration, data and processes modules' migrations applied.</summary>
public sealed class DataDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlFixture _database = new();

    public string ConnectionString => _database.ConnectionString;

    public DataDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    /// <summary>A data context over <paramref name="connection"/>, which the caller owns.</summary>
    public static DataDbContext CreateContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<DataDbContext>()
            .UseNpgsql(connection)
            .Options);

    /// <summary>A configuration context over <paramref name="connection"/>, which the caller owns.</summary>
    public static ConfigurationDbContext CreateConfigurationContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(connection)
            .Options);

    /// <summary>
    /// Applies the configuration and data migrations to the database that <paramref name="schemaConnectionString"/>
    /// names and gives the runtime role its rights, as a server does for a tenant database.
    /// </summary>
    public static async Task MigrateTenantAsync(string schemaConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(schemaConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var configuration = CreateConfigurationContext(connection);
        await using var data = CreateContext(connection);
        await configuration.Database.MigrateAsync(cancellationToken);
        await data.Database.MigrateAsync(cancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(schemaConnectionString, cancellationToken);
    }

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await using (var configuration = CreateConfigurationContext())
        {
            await configuration.Database.MigrateAsync();
        }

        await using var data = CreateContext();
        await data.Database.MigrateAsync();

        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
        await processes.Database.MigrateAsync();
        await PostgreSqlServer.GrantRuntimeRoleAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
}
