using Axis.Shared.Application.Tenancy;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.Testing;
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

        TestTenantContext tenantContext = new(TestSchema);
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowBuilderDbContext>(
            ConnectionString,
            opts => new WorkflowBuilderDbContext(opts, tenantContext));
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal WorkflowBuilderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkflowBuilderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new WorkflowBuilderDbContext(options, new TestTenantContext(TestSchema));
    }
}

internal sealed class TestTenantContext(string schemaName) : ITenantContext
{
    public Guid OrganizationId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string SchemaName => schemaName;
}
