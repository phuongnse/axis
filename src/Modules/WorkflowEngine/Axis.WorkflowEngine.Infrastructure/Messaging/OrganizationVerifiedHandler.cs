using axis.identity.events;
using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="OrganizationVerifiedEvent"/> from Kafka and provisions the
/// WorkflowEngine tenant schema for the verified organization (ADR-019, ADR-023).
/// </summary>
internal sealed class OrganizationVerifiedHandler(
    IConfiguration configuration,
    ILogger<OrganizationVerifiedHandler> logger)
{
    public async Task Handle(OrganizationVerifiedEvent evt, CancellationToken cancellationToken)
    {
        Guid organizationId = evt.OrganizationId();
        string schema = $"tenant_{organizationId:N}";
        string connectionString = configuration.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("Missing connection string 'WorkflowEngine'.");

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync(cancellationToken);

        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<WorkflowEngineDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using WorkflowEngineDbContext context = new(optionsBuilder.Options, tenantContext);
        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation(
            "WorkflowEngine tenant schema {Schema} provisioned for organization {OrganizationId}",
            schema, organizationId);
    }
}
