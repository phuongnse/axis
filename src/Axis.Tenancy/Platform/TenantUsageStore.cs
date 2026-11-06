using Npgsql;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Writes the usage of a cell's tenants to <c>platform.tenant_usage_latest</c> and <c>platform.tenant_usage</c> over
/// the platform database, and reads a tenant's latest storage sample and its day rows. It writes counts and sizes only, among them the counts of failed work. The platform
/// role that the data source connects as has the write grant of <see cref="PlatformRoleGrants"/> on these two tables.
/// </summary>
public sealed class TenantUsageStore(NpgsqlDataSource platform)
{
    /// <summary>How many months of day rows a tenant keeps.</summary>
    public const int RetentionMonths = 13;

    /// <summary>
    /// Records a storage sample of the tenant and its <paramref name="failedWork"/> counts in one transaction. The sample and the
    /// counts replace the tenant's latest row, and the day rows keep sizes only. The
    /// row of the UTC day of <paramref name="now"/> takes the sample when its total is larger than the day's so far,
    /// so it keeps the day's largest sample. The tenant's day rows older than <see cref="RetentionMonths"/> months are deleted.
    /// </summary>
    public async Task RecordSampleAsync(string tenantId, long databaseBytes, long fileBytes, TenantFailedWorkCounts failedWork, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        await using var connection = await platform.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var latest = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_usage_latest
                (tenant_id, database_bytes, file_bytes, failed_instances, unknown_calls, open_dead_letters, sampled_at)
            VALUES (@tenant, @database, @files, @instances, @calls, @deadLetters, @now)
            ON CONFLICT (tenant_id) DO UPDATE
            SET database_bytes = EXCLUDED.database_bytes, file_bytes = EXCLUDED.file_bytes,
                failed_instances = EXCLUDED.failed_instances, unknown_calls = EXCLUDED.unknown_calls,
                open_dead_letters = EXCLUDED.open_dead_letters, sampled_at = EXCLUDED.sampled_at
            """,
            connection,
            transaction))
        {
            latest.Parameters.AddWithValue("tenant", tenantId);
            latest.Parameters.AddWithValue("database", databaseBytes);
            latest.Parameters.AddWithValue("files", fileBytes);
            latest.Parameters.AddWithValue("instances", failedWork.Instances);
            latest.Parameters.AddWithValue("calls", failedWork.UnknownCalls);
            latest.Parameters.AddWithValue("deadLetters", failedWork.DeadLetters);
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
    /// The tenant's day rows from <paramref name="from"/> on, newest first. The rows hold sizes and counts only.
    /// </summary>
    public async Task<IReadOnlyList<TenantUsageRow>> ListDaysAsync(string tenantId, DateOnly from, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await using var command = platform.CreateCommand(
            """
            SELECT day, database_bytes, file_bytes, users, applications, emails_sent, connector_calls, work_items_run
            FROM platform.tenant_usage
            WHERE tenant_id = @tenant AND day >= @from
            ORDER BY day DESC
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("from", from);
        var rows = new List<TenantUsageRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new TenantUsageRow
            {
                TenantId = tenantId,
                Day = reader.GetFieldValue<DateOnly>(0),
                DatabaseBytes = reader.GetInt64(1),
                FileBytes = reader.GetInt64(2),
                Users = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Applications = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                EmailsSent = reader.GetInt32(5),
                ConnectorCalls = reader.GetInt64(6),
                WorkItemsRun = reader.GetInt64(7),
            });
        }

        return rows;
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
