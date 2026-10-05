using Axis.Configuration.Storage;
using Axis.Data.Storage;
using Microsoft.EntityFrameworkCore;

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
