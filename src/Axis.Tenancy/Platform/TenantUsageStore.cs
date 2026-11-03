using Npgsql;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Writes the usage of a cell's tenants to <c>platform.tenant_usage_latest</c> and <c>platform.tenant_usage</c> over
/// the platform database, and reads a tenant's latest storage sample. It writes counts and sizes only. The platform
/// role that the data source connects as has the write grant of <see cref="PlatformRoleGrants"/> on these two tables.
/// </summary>
public sealed class TenantUsageStore(NpgsqlDataSource platform)
{
    /// <summary>How many months of day rows a tenant keeps.</summary>
    public const int RetentionMonths = 13;

    /// <summary>
    /// Records a storage sample of the tenant in one transaction. The sample replaces the tenant's latest row. The
    /// row of the UTC day of <paramref name="now"/> takes the sample when its total is larger than the day's so far,
    /// so it keeps the day's largest sample. The tenant's day rows older than <see cref="RetentionMonths"/> months are deleted.
    /// </summary>
    public async Task RecordSampleAsync(string tenantId, long databaseBytes, long fileBytes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        await using var connection = await platform.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var latest = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at)
            VALUES (@tenant, @database, @files, @now)
            ON CONFLICT (tenant_id) DO UPDATE
            SET database_bytes = EXCLUDED.database_bytes, file_bytes = EXCLUDED.file_bytes, sampled_at = EXCLUDED.sampled_at
            """,
            connection,
            transaction))
        {
            latest.Parameters.AddWithValue("tenant", tenantId);
            latest.Parameters.AddWithValue("database", databaseBytes);
            latest.Parameters.AddWithValue("files", fileBytes);
            latest.Parameters.AddWithValue("now", now.UtcDateTime);
            await latest.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var day = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_usage AS u (tenant_id, day, database_bytes, file_bytes)
            VALUES (@tenant, @day, @database, @files)
            ON CONFLICT (tenant_id, day) DO UPDATE
            SET database_bytes = CASE WHEN EXCLUDED.database_bytes + EXCLUDED.file_bytes > u.database_bytes + u.file_bytes
                    THEN EXCLUDED.database_bytes ELSE u.database_bytes END,
                file_bytes = CASE WHEN EXCLUDED.database_bytes + EXCLUDED.file_bytes > u.database_bytes + u.file_bytes
                    THEN EXCLUDED.file_bytes ELSE u.file_bytes END
            """,
            connection,
            transaction))
        {
            day.Parameters.AddWithValue("tenant", tenantId);
            day.Parameters.AddWithValue("day", today);
            day.Parameters.AddWithValue("database", databaseBytes);
            day.Parameters.AddWithValue("files", fileBytes);
            await day.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var retention = new NpgsqlCommand(
            "DELETE FROM platform.tenant_usage WHERE tenant_id = @tenant AND day < @cutoff",
            connection,
            transaction))
        {
            retention.Parameters.AddWithValue("tenant", tenantId);
            retention.Parameters.AddWithValue("cutoff", today.AddMonths(-RetentionMonths));
            await retention.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The tenant's latest storage sample as database bytes plus file bytes, or <c>null</c> when the tenant has no
    /// sample yet.
    /// </summary>
    public async Task<long?> LatestStorageBytesAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await using var command = platform.CreateCommand(
            "SELECT database_bytes + file_bytes FROM platform.tenant_usage_latest WHERE tenant_id = @tenant");
        command.Parameters.AddWithValue("tenant", tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) as long?;
    }

    /// <summary>
    /// Adds one work item to the UTC day's count of the tenant, and one connector or package call when
    /// <paramref name="connectorCall"/> is true. The day's row is created when it does not exist yet.
    /// </summary>
    public async Task CountWorkItemAsync(string tenantId, bool connectorCall, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await using var connection = await platform.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_usage AS u (tenant_id, day, connector_calls, work_items_run)
            VALUES (@tenant, @day, @calls, 1)
            ON CONFLICT (tenant_id, day) DO UPDATE
            SET work_items_run = u.work_items_run + 1, connector_calls = u.connector_calls + @calls
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("day", DateOnly.FromDateTime(now.UtcDateTime));
            command.Parameters.AddWithValue("calls", connectorCall ? 1L : 0L);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
