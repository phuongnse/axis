using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.DataModeling.Infrastructure.Repositories;
using Axis.Shared.Infrastructure.Persistence;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.DataModeling.Infrastructure.Tests.Persistence;

/// <summary>
/// E01 F03 — schema-per-tenant: data in tenant_alpha is not readable from tenant_beta (US-008).
/// </summary>
public sealed class TenantSchemaIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;
    private readonly Guid _orgAlpha = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _orgBeta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            "axis_datamodeling_schema_iso_test");

        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand createSchemas = connection.CreateCommand();
        string schemaAlpha = new FixedTenantContext(_orgAlpha).SchemaName;
        string schemaBeta = new FixedTenantContext(_orgBeta).SchemaName;
        createSchemas.CommandText =
            $"""
             CREATE SCHEMA IF NOT EXISTS "{schemaAlpha}";
             CREATE SCHEMA IF NOT EXISTS "{schemaBeta}";
             """;
        await createSchemas.ExecuteNonQueryAsync();

        await MigrateSchemaAsync(_orgAlpha);
        await MigrateSchemaAsync(_orgBeta);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task DataModelRepository_WhenModelInAnotherTenantSchema_IsNotVisible()
    {
        Guid modelId;
        await using (DataModelingDbContext ctxAlpha = CreateContext(_orgAlpha))
        {
            DataModelRepository repo = new(ctxAlpha);
            DataModel model = DataModel.Create("AlphaOnly", null, null, null, _orgAlpha, "user-1");
            await repo.AddAsync(model);
            await ctxAlpha.SaveChangesAsync();
            modelId = model.Id;
        }

        await using DataModelingDbContext ctxBeta = CreateContext(_orgBeta);
        DataModelRepository betaRepo = new(ctxBeta);

        DataModel? fromBeta = await betaRepo.GetByIdAsync(modelId, _orgBeta);
        DataModel? crossOrgLookup = await betaRepo.GetByIdAsync(modelId, _orgAlpha);

        fromBeta.Should().BeNull();
        crossOrgLookup.Should().BeNull();
    }

    private async Task MigrateSchemaAsync(Guid organizationId)
    {
        FixedTenantContext tenant = new(organizationId);
        await PostgresModuleTestDatabase.MigrateAsync<DataModelingDbContext>(
            _connectionString,
            opts => new DataModelingDbContext(opts, tenant));
    }

    private DataModelingDbContext CreateContext(Guid organizationId)
    {
        FixedTenantContext tenant = new(organizationId);
        TenantSchemaInterceptor interceptor = new(tenant);
        DbContextOptions<DataModelingDbContext> options = new DbContextOptionsBuilder<DataModelingDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new DataModelingDbContext(options, tenant);
    }
}
