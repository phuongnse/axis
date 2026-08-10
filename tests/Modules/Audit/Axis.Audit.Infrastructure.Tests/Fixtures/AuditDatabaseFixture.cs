using Axis.Audit.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Axis.Audit.Infrastructure.Tests.Fixtures;

public sealed class AuditDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public AuditDbContext CreateContext()
    {
        DbContextOptions<AuditDbContext> options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new AuditDbContext(options);
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_audit_infra_test");
        await PostgresModuleTestDatabase.MigrateAsync<AuditDbContext>(
            _connectionString,
            options => new AuditDbContext(options));
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();
}

[CollectionDefinition("AuditDb")]
public sealed class AuditDatabaseCollection : ICollectionFixture<AuditDatabaseFixture>;
