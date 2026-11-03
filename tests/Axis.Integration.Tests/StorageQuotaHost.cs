using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Makes the tenant quotas of a test host those of a cell. A fixture host reads its tenants from <c>Tenants:*</c>, which a
/// cell never does, so it cannot be set up as a real cell. <see cref="TenantQuotas"/> is the only service of the server that
/// reads the cell, so replacing it is enough for the storage check.
/// </summary>
internal static class StorageQuotaHost
{
    private static readonly CellOptions _cell = new() { Id = "vn-1", Region = "vn", TenantDomain = "vn.example.test" };

    /// <summary>Gives every tenant of the host the storage quota <paramref name="limit"/>, as the cell's default.</summary>
    public static IWebHostBuilder InCell(this IWebHostBuilder builder, long limit)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{QuotaDefaults.SectionName}:{QuotaKeys.Storage}"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .Build();
        return builder.ConfigureTestServices(services =>
            services.AddSingleton(provider => new TenantQuotas(_cell, QuotaDefaults.Load(configuration, _cell), provider.GetRequiredService<ITenantCatalog>())));
    }

    /// <summary>
    /// Sets the latest storage sample of <paramref name="tenant"/> in the platform tables of the database at
    /// <paramref name="connectionString"/>, where the test server reads them. A fixture does not migrate the platform
    /// database, so the first call creates its tables there, and a tenant's first call adds its tenant row.
    /// </summary>
    public static async Task SeedUsageAsync(string connectionString, string tenant, long databaseBytes, long fileBytes, CancellationToken cancellationToken)
    {
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(cancellationToken);

            // The sample belongs to a tenant row. The server here reads its tenants from its settings, so the row holds only its id.
            if (!await context.Tenants.AnyAsync(row => row.Id == tenant, cancellationToken))
            {
                context.Tenants.Add(new TenantRow { Id = tenant, Status = "active", SchemaVersion = TenantSchemaVersion.Current });
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at)
            VALUES (@tenant, @database, @files, now())
            ON CONFLICT (tenant_id) DO UPDATE
            SET database_bytes = EXCLUDED.database_bytes, file_bytes = EXCLUDED.file_bytes, sampled_at = EXCLUDED.sampled_at
            """,
            connection);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("database", databaseBytes);
        command.Parameters.AddWithValue("files", fileBytes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Deletes every latest storage sample in the platform tables of the database at <paramref name="connectionString"/>.</summary>
    public static async Task ClearUsageAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM platform.tenant_usage_latest", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
