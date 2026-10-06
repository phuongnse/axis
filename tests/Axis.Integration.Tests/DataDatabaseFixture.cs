using Axis.Configuration.Storage;
using Axis.Data.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>A real PostgreSQL database with the configuration and data modules' migrations applied.</summary>
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

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await using (var configuration = CreateConfigurationContext())
        {
            await configuration.Database.MigrateAsync();
        }

        await using var data = CreateContext();
        await data.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
}
