using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application.Tenancy;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Api.Infrastructure;

/// <summary>US-003: Creates tenant schema and migrates all module databases idempotently.</summary>
internal sealed class TenantSchemaProvisioner(
    IConfiguration configuration,
    ILogger<TenantSchemaProvisioner> logger) : ITenantSchemaProvisioner
{
    public async Task ProvisionAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        string schema = $"tenant_{organizationId:N}";
        logger.LogInformation(
            "Provisioning tenant schema {Schema} for organization {OrganizationId}",
            schema,
            organizationId);

        await ProvisionDataModelingAsync(organizationId, cancellationToken);
        await ProvisionWorkflowBuilderAsync(organizationId, cancellationToken);
        await ProvisionFormBuilderAsync(organizationId, cancellationToken);
        await ProvisionWorkflowEngineAsync(organizationId, cancellationToken);

        logger.LogInformation("Tenant schema {Schema} provisioned successfully", schema);
    }

    private static async Task CreateSchemaIfNotExistsAsync(
        string connectionString,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        string schema = $"tenant_{organizationId:N}";
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ProvisionDataModelingAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("DataModeling")
            ?? throw new InvalidOperationException("Missing connection string 'DataModeling'.");
        await CreateSchemaIfNotExistsAsync(connectionString, organizationId, cancellationToken);

        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<DataModelingDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using DataModelingDbContext context = new(optionsBuilder.Options, tenantContext);
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    private async Task ProvisionWorkflowBuilderAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("WorkflowBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowBuilder'.");
        await CreateSchemaIfNotExistsAsync(connectionString, organizationId, cancellationToken);

        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<WorkflowBuilderDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using WorkflowBuilderDbContext context = new(optionsBuilder.Options, tenantContext);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task ProvisionFormBuilderAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'FormBuilder'.");
        await CreateSchemaIfNotExistsAsync(connectionString, organizationId, cancellationToken);

        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<FormBuilderDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using FormBuilderDbContext context = new(optionsBuilder.Options, tenantContext);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task ProvisionWorkflowEngineAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowEngine'.");
        await CreateSchemaIfNotExistsAsync(connectionString, organizationId, cancellationToken);

        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<WorkflowEngineDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using WorkflowEngineDbContext context = new(optionsBuilder.Options, tenantContext);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
