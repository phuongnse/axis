using Axis.Solutions.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Solutions.Infrastructure.Tests.Fixtures;

public sealed class SolutionsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _connectionString = null!;
    public string ConnectionString => _connectionString;
    public SolutionsDbContext CreateContext() => new(new DbContextOptionsBuilder<SolutionsDbContext>().UseNpgsql(_connectionString).Options);
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(_postgres.GetConnectionString(), "axis_solutions_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<SolutionsDbContext>(_connectionString, options => new SolutionsDbContext(options));
    }
    public ValueTask DisposeAsync() => _postgres.DisposeAsync();
}

[CollectionDefinition("SolutionsDb")]
public sealed class SolutionsDatabaseCollection : ICollectionFixture<SolutionsDatabaseFixture>;
