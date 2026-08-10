using Axis.Authorization.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Authorization.Infrastructure.Tests.Fixtures;

public sealed class AuthorizationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public AuthorizationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(_connectionString)
            .Options);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_authorization_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<AuthorizationDbContext>(
            _connectionString,
            options => new AuthorizationDbContext(options));
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();
}

[CollectionDefinition("AuthorizationDb")]
public sealed class AuthorizationDatabaseCollection
    : ICollectionFixture<AuthorizationDatabaseFixture>;
