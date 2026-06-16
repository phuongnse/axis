using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace Axis.Shared.Infrastructure.Tests.Persistence;

public class WorkspaceSchemaInterceptorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DbContext BuildContext(string schemaName)
    {
        IWorkspaceContext workspaceContext = Substitute.For<IWorkspaceContext>();
        workspaceContext.SchemaName.Returns(schemaName);
        WorkspaceSchemaInterceptor interceptor = new WorkspaceSchemaInterceptor(workspaceContext);
        DbContextOptions<ProbeDbContext> options = new DbContextOptionsBuilder<ProbeDbContext>()
                    .UseNpgsql(_postgres.GetConnectionString())
                    .AddInterceptors(interceptor)
                    .Options;

        return new ProbeDbContext(options);
    }

    [Fact]
    public async Task WorkspaceSchemaInterceptor_WhenConnectionOpened_UsesWorkspaceSchema()
    {
        const string schema = "workspace_acme";
        await using DbContext ctx = BuildContext(schema);

        // Ensure schema exists
        await ctx.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"");
        string result = await ctx.Database
                    .SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"")
                    .FirstAsync();

        result.Should().Be(schema);
    }

    [Fact]
    public async Task WorkspaceSchemaInterceptor_WhenMultipleContexts_EachUsesDifferentSchema()
    {
        const string schemaA = "workspace_alpha";
        const string schemaB = "workspace_beta";

        await using DbContext ctxA = BuildContext(schemaA);
        await using DbContext ctxB = BuildContext(schemaB);

        await ctxA.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaA}\"");
        await ctxB.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaB}\"");
        string resultA = await ctxA.Database
                    .SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"")
                    .FirstAsync();
        string resultB = await ctxB.Database
                    .SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"")
                    .FirstAsync();

        resultA.Should().Be(schemaA);
        resultB.Should().Be(schemaB);
    }

    // Minimal DbContext used only for sending raw SQL
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options)
        : DbContext(options);
}
