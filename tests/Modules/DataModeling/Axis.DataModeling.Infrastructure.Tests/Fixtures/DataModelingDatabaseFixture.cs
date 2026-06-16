using Axis.DataModeling.Infrastructure.Persistence;
using Axis.Shared.Application.Workspaces;
using Axis.Testing;
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
    private string _connectionString = null!;

    public IWorkspaceContext WorkspaceContext { get; } = new TestWorkspaceContext(TestSchema);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_datamodeling_infra_test");

        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await command.ExecuteNonQueryAsync();

        await PostgresModuleTestDatabase.MigrateAsync<DataModelingDbContext>(
            _connectionString,
            opts => new DataModelingDbContext(opts, WorkspaceContext));
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal DataModelingDbContext CreateContext()
    {
        DbContextOptions<DataModelingDbContext> options = new DbContextOptionsBuilder<DataModelingDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new DataModelingDbContext(options, WorkspaceContext);
    }
}

internal sealed class TestWorkspaceContext(string schemaName) : IWorkspaceContext
{
    public Guid workspaceId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
