using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.BusinessObjects.Infrastructure.Tests.Fixtures;

public sealed class BusinessObjectsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public BusinessObjectsDbContext CreateContext()
    {
        DbContextOptions<BusinessObjectsDbContext> options = new DbContextOptionsBuilder<BusinessObjectsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new BusinessObjectsDbContext(options);
    }

    public Task<string> CreateDatabaseAsync(string databaseName) =>
        PostgresModuleTestDatabase.CreateAsync(_postgres.GetConnectionString(), databaseName);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_business_objects_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<BusinessObjectsDbContext>(
            _connectionString,
            opts => new BusinessObjectsDbContext(opts));
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
