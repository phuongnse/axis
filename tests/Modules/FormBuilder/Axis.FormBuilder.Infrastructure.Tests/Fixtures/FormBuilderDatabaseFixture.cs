using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application.Workspaces;
using Axis.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.FormBuilder.Infrastructure.Tests.Fixtures;

public sealed class FormBuilderDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private const string TestSchema = "test_form_builder";
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_formbuilder_infra_test");

        await using NpgsqlConnection conn = new(ConnectionString);
        await conn.OpenAsync();

        await using NpgsqlCommand schemaCmd = conn.CreateCommand();
        schemaCmd.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await schemaCmd.ExecuteNonQueryAsync();

        TestWorkspaceContext workspaceContext = new(TestSchema);
        await PostgresModuleTestDatabase.MigrateAsync<FormBuilderDbContext>(
            ConnectionString,
            opts => new FormBuilderDbContext(opts, workspaceContext));

        // Create a minimal workflow_definitions table so IsReferencedByWorkflowAsync can query it
        await using NpgsqlCommand wfCmd = conn.CreateCommand();
        wfCmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{TestSchema}".workflow_definitions (
                id UUID PRIMARY KEY,
                steps JSONB NOT NULL DEFAULT '[]'::jsonb,
                deleted_at TIMESTAMPTZ NULL
            );
            """;
        await wfCmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal FormBuilderDbContext CreateContext()
    {
        DbContextOptions<FormBuilderDbContext> options = new DbContextOptionsBuilder<FormBuilderDbContext>()
                    .UseNpgsql(ConnectionString)
                    .Options;
        return new FormBuilderDbContext(options, new TestWorkspaceContext(TestSchema));
    }
}

internal sealed class TestWorkspaceContext(string schemaName) : IWorkspaceContext
{
    public Guid workspaceId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
