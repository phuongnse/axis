using Axis.Objects.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Objects.Infrastructure.Tests.Fixtures;

public sealed class ObjectsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public ObjectsDbContext CreateContext()
    {
        DbContextOptions<ObjectsDbContext> options = new DbContextOptionsBuilder<ObjectsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new ObjectsDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_objects_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<ObjectsDbContext>(
            _connectionString,
            opts => new ObjectsDbContext(opts));
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
