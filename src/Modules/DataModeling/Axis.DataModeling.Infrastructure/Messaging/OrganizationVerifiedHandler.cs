using axis.identity.events;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.DataModeling.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="OrganizationVerifiedEvent"/> from Kafka and provisions the
/// DataModeling tenant schema for the verified organization (ADR-019, ADR-023).
/// Idempotent: CREATE SCHEMA IF NOT EXISTS + EF MigrateAsync.
/// </summary>
internal sealed class OrganizationVerifiedHandler(
    IConfiguration configuration,
    ILogger<OrganizationVerifiedHandler> logger)
{
    public async Task Handle(OrganizationVerifiedEvent evt, CancellationToken cancellationToken)
    {
        Guid organizationId = evt.OrganizationId();
        string schema = $"tenant_{organizationId:N}";
        string connectionString = configuration.GetConnectionString("DataModeling")
            ?? throw new InvalidOperationException("Missing connection string 'DataModeling'.");

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync(cancellationToken);

        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<DataModelingDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using DataModelingDbContext context = new(optionsBuilder.Options, tenantContext);
        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation(
            "DataModeling tenant schema {Schema} provisioned for organization {OrganizationId}",
            schema, organizationId);
    }
}
