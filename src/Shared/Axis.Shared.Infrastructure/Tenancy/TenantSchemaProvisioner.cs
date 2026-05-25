using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Shared steps for per-module tenant schema provisioning (US-003). Each module supplies its own DbContext factory.
/// </summary>
public static class TenantSchemaProvisioner
{
    public static async Task ProvisionAsync(
        string connectionString,
        string advisoryLockKey,
        Guid organizationId,
        Func<CancellationToken, Task> migrateTenantSchemaAsync,
        ILogger logger,
        string moduleName,
        CancellationToken cancellationToken)
    {
        string schema = $"tenant_{organizationId:N}";

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand acquireLock = connection.CreateCommand();
        acquireLock.CommandText = "SELECT pg_advisory_lock(hashtextextended(@key, 0));";
        acquireLock.Parameters.AddWithValue("key", advisoryLockKey);
        await acquireLock.ExecuteNonQueryAsync(cancellationToken);

        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync(cancellationToken);

        await migrateTenantSchemaAsync(cancellationToken);

        logger.LogInformation(
            "{Module} tenant schema {Schema} provisioned for organization {OrganizationId}",
            moduleName, schema, organizationId);
    }

    public static async Task MigrateWithFixedTenantAsync<TContext>(
        string connectionString,
        Guid organizationId,
        Func<DbContextOptions<TContext>, TContext> createContext,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        FixedTenantContext tenantContext = new(organizationId);
        DbContextOptionsBuilder<TContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using TContext context = createContext(optionsBuilder.Options);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
