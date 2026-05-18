using Axis.Shared.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.DataModeling.Infrastructure.Tests.Fixtures;

public sealed class DataModelingDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private const string TestSchema = "test_data_modeling";
    public string ConnectionString { get; private set; } = null!;
    public ITenantContext TenantContext { get; } = new TestTenantContext(TestSchema);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await cmd.ExecuteNonQueryAsync();

        using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal DataModelingDbContext CreateContext()
    {
        DbContextOptions<DataModelingDbContext> options = new DbContextOptionsBuilder<DataModelingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new DataModelingDbContext(options, TenantContext);
    }
}

internal sealed class TestTenantContext(string schemaName) : ITenantContext
{
    public Guid OrganizationId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
