using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Testing;

/// <summary>
/// Deterministic per-organization tenant schema setup for integration tests.
/// Mirrors production <see cref="TenantSchemaProvisioner"/> without advisory locks or logging.
/// </summary>
public static class TenantTestProvisioner
{
    public static async Task MigrateTenantSchemaAsync<TContext>(
        string connectionString,
        Guid organizationId,
        Func<DbContextOptions<TContext>, TContext> createContext,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        string schema = $"tenant_{organizationId:N}";

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync(cancellationToken);

        await TenantSchemaProvisioner.MigrateWithFixedTenantAsync(
            connectionString,
            organizationId,
            createContext,
            cancellationToken);
    }
}
