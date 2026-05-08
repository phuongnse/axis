using Axis.Shared.Application.Tenancy;
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
        ConnectionString = _postgres.GetConnectionString();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var schemaCmd = conn.CreateCommand();
        schemaCmd.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{TestSchema}";""";
        await schemaCmd.ExecuteNonQueryAsync();

        using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        // Create a minimal workflow_definitions table so IsReferencedByWorkflowAsync can query it
        await using var wfCmd = conn.CreateCommand();
        wfCmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{TestSchema}".workflow_definitions (
                id UUID PRIMARY KEY,
                steps JSONB NOT NULL DEFAULT '[]'::jsonb
            );
            """;
        await wfCmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal FormBuilderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FormBuilderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new FormBuilderDbContext(options, new TestTenantContext(TestSchema));
    }
}

internal sealed class TestTenantContext(string schemaName) : ITenantContext
{
    public Guid OrganizationId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string OrganizationSlug => "test-org";
    public string SchemaName => schemaName;
}
