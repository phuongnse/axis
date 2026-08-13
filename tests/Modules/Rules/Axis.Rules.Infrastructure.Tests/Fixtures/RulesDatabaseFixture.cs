using Axis.Rules.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Rules.Infrastructure.Tests.Fixtures;

public sealed class RulesDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public RulesDbContext CreateContext()
    {
        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new RulesDbContext(options);
    }

    public Task<string> CreateDatabaseAsync(string databaseName) =>
        PostgresModuleTestDatabase.CreateAsync(_postgres.GetConnectionString(), databaseName);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_rules_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<RulesDbContext>(
            _connectionString,
            options => new RulesDbContext(options));
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();
}

[CollectionDefinition("RulesDb")]
public sealed class RulesDatabaseCollection : ICollectionFixture<RulesDatabaseFixture>;
