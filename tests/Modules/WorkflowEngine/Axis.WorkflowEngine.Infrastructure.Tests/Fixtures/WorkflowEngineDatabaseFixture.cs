using Axis.Shared.Application.Tenancy;
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
        ConnectionString = _postgres.GetConnectionString();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var schemaCmd = conn.CreateCommand();
        schemaCmd.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await schemaCmd.ExecuteNonQueryAsync();

        using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        // Stub table for cross-module WorkflowDefinitionReader queries
        await using var wfCmd = conn.CreateCommand();
        wfCmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{TestSchema}".workflow_definitions (
                id UUID PRIMARY KEY,
                organization_id UUID NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft'
            );
            """;
        await wfCmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal WorkflowEngineDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new WorkflowEngineDbContext(options, new TestTenantContext(TestSchema));
    }
}

internal sealed class TestTenantContext(string schemaName) : ITenantContext
{
    public Guid OrganizationId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
