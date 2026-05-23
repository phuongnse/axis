using Axis.Identity.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Identity.Infrastructure.Tests.Fixtures;

public sealed class IdentityDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public IdentityDbContext CreateContext()
    {
        DbContextOptions<IdentityDbContext> options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new IdentityDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_identity_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<IdentityDbContext>(
            _connectionString,
            opts => new IdentityDbContext(opts));
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
