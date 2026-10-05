using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Axis.Integration.Tests;

/// <summary>A real PostgreSQL database with the configuration module's migrations applied.</summary>
public sealed class ConfigurationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlFixture _database = new();

    public string ConnectionString => _database.ConnectionString;

    public ConfigurationDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(interceptors)
            .Options);

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
}
