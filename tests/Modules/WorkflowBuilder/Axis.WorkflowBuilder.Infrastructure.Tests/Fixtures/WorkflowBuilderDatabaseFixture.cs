using Axis.Shared.Application.Workspaces;
using Axis.Testing;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.WorkflowBuilder.Infrastructure.Tests.Fixtures;

public sealed class WorkflowBuilderDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private const string TestSchema = "test_workflow_builder";
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_workflowbuilder_infra_test");

        await using NpgsqlConnection conn = new(ConnectionString);
        await conn.OpenAsync();
        await using NpgsqlCommand command = conn.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await command.ExecuteNonQueryAsync();

        TestWorkspaceContext workspaceContext = new(TestSchema);
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowBuilderDbContext>(
            ConnectionString,
            opts => new WorkflowBuilderDbContext(opts, workspaceContext));
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal WorkflowBuilderDbContext CreateContext()
    {
        DbContextOptions<WorkflowBuilderDbContext> options = new DbContextOptionsBuilder<WorkflowBuilderDbContext>()
                    .UseNpgsql(ConnectionString)
                    .Options;
        return new WorkflowBuilderDbContext(options, new TestWorkspaceContext(TestSchema));
    }
}

internal sealed class TestWorkspaceContext(string schemaName) : IWorkspaceContext
{
    public Guid workspaceId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
