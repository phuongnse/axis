using Axis.Shared.Application.Tenancy;
using Axis.Testing;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.WorkflowEngine.Infrastructure.Tests.Fixtures;

public sealed class WorkflowEngineDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private const string TestSchema = "test_workflow_engine";
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_workflowengine_infra_test");

        await using NpgsqlConnection conn = new(ConnectionString);
        await conn.OpenAsync();

        await using NpgsqlCommand schemaCmd = conn.CreateCommand();
        schemaCmd.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await schemaCmd.ExecuteNonQueryAsync();

        TestTenantContext tenantContext = new(TestSchema);
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowEngineDbContext>(
            ConnectionString,
            opts => new WorkflowEngineDbContext(opts, tenantContext));

        // Stub table for cross-module WorkflowDefinitionReader queries
        await using NpgsqlCommand wfCmd = conn.CreateCommand();
        wfCmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{TestSchema}".workflow_definitions (
                id UUID PRIMARY KEY,
                team_account_id UUID NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft'
            );
            """;
        await wfCmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal WorkflowEngineDbContext CreateContext()
    {
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
                    .UseNpgsql(ConnectionString)
                    .Options;
        return new WorkflowEngineDbContext(options, new TestTenantContext(TestSchema));
    }
}

internal sealed class TestTenantContext(string schemaName) : ITenantContext
{
    public Guid TeamAccountId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
