using Axis.Rules.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Rules.Infrastructure.Tests.Fixtures;

public sealed class RulesDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public RulesDbContext CreateContext()
    {
        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new RulesDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_rules_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<RulesDbContext>(
            _connectionString,
            options => new RulesDbContext(options));
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

[CollectionDefinition("RulesDb")]
public sealed class RulesDatabaseCollection : ICollectionFixture<RulesDatabaseFixture>;
