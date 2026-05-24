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

        // Serialize per-org provisioning via session-scoped advisory lock so two
        // concurrent handler instances (Kafka at-least-once redelivery) don't race
        // on __EFMigrationsHistory. The lock is held by THIS connection and blocks
        // any other connection trying to acquire the same key; MigrateAsync opens
        // its own connection but is gated by the lock at the DB level. The lock
        // auto-releases when this connection closes (await using).
        await using NpgsqlCommand acquireLock = connection.CreateCommand();
        acquireLock.CommandText = "SELECT pg_advisory_lock(hashtextextended(@key, 0));";
        acquireLock.Parameters.AddWithValue("key", $"axis.tenant.datamodeling:{organizationId:N}");
        await acquireLock.ExecuteNonQueryAsync(cancellationToken);

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
