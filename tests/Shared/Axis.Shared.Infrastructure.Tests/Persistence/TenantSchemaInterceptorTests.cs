using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace Axis.Shared.Infrastructure.Tests.Persistence;

public class TenantSchemaInterceptorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DbContext BuildContext(string schemaName)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.SchemaName.Returns(schemaName);

        var interceptor = new TenantSchemaInterceptor(tenantContext);

        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        return new ProbeDbContext(options);
    }

    [Fact]
    public async Task Connection_uses_tenant_schema_after_open()
    {
        const string schema = "tenant_acme";
        await using var ctx = BuildContext(schema);

        // Ensure schema exists
        await ctx.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"");

        var result = await ctx.Database
            .SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"")
            .FirstAsync();

        result.Should().Be(schema);
    }

    [Fact]
    public async Task Different_contexts_use_different_schemas()
    {
        const string schemaA = "tenant_alpha";
        const string schemaB = "tenant_beta";

        await using var ctxA = BuildContext(schemaA);
        await using var ctxB = BuildContext(schemaB);

        await ctxA.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaA}\"");
        await ctxB.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaB}\"");

        var resultA = await ctxA.Database
            .SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"")
            .FirstAsync();

        var resultB = await ctxB.Database
            .SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"")
            .FirstAsync();

        resultA.Should().Be(schemaA);
        resultB.Should().Be(schemaB);
    }

    // Minimal DbContext used only for sending raw SQL
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options)
        : DbContext(options);
}
