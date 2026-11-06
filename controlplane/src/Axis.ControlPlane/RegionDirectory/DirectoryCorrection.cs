using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Npgsql;
using NpgsqlTypes;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Corrects the region directory from the tenant list of each cell, see <c>docs/reference/region-directory.md#picking-a-cell</c>.
/// For each cell that answers, one transaction adds the tenants and hosts the directory lacks, removes those the cell no longer
/// lists and sets the cell's count to the length of the list. A cell that cannot be reached or answers an error keeps its rows, a
/// warning log names it and the job goes on to the next cell, so the job still succeeds. A tenant or a host that the directory holds
/// under another cell is left alone and counted in a warning log, as a person must decide which cell owns it. The job writes
/// nothing to <c>directory.held</c>. After each commit it queues a <c>writeTenantRecord</c> job for each tenant it added and a
/// <c>removeTenantRecord</c> job for each it removed, on the control plane database, see <see cref="TenantRecordJobs"/>. A database error fails the attempt, and the job runner queues it again.
/// </summary>
internal sealed partial class DirectoryCorrection(
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    NpgsqlDataSource controlPlane,
    CellClients cells,
    ILogger<DirectoryCorrection> logger) : IJobHandler
{
    public const string JobKind = "correctDirectory";

    public string Kind => JobKind;

    /// <summary>Corrects every cell of the region. The payload is not used.</summary>
    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        foreach (var client in cells.All)
        {
            IReadOnlyList<CellTenant> tenants;
            try
            {
                tenants = await client.ListTenantsAsync(cancellationToken);
            }
            catch (CellCallException exception)
            {
                // Also a transport failure or a timeout. The next run is 5 minutes away.
                LogListFailed(exception, client.CellId);
                continue;
            }

            await CorrectAsync(client.CellId, tenants, cancellationToken);
        }
    }

    private async Task CorrectAsync(string cellId, IReadOnlyList<CellTenant> tenants, CancellationToken cancellationToken)
    {
        var ids = tenants.Select(tenant => tenant.Id).Distinct(StringComparer.Ordinal).ToArray();
        var hostPairs = tenants
            .SelectMany(tenant => tenant.Hosts.Select(host => (Host: host.ToLowerInvariant(), TenantId: tenant.Id)))
            .DistinctBy(pair => pair.Host, StringComparer.Ordinal)
            .ToArray();
        var hosts = hostPairs.Select(pair => pair.Host).ToArray();
        var hostTenants = hostPairs.Select(pair => pair.TenantId).ToArray();

        await using var connection = await directory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The lock serializes the correction with the cell picker, which raises the count of the same row.
        string? region = null;
        string? tenantDomain = null;
        await using (var lockRow = new NpgsqlCommand(
            """
            SELECT c.region_code, r.tenant_domain FROM directory.cells c JOIN directory.regions r ON r.code = c.region_code
            WHERE c.id = @cell FOR UPDATE OF c
            """, connection, transaction))
        {
            lockRow.Parameters.AddWithValue("cell", cellId);
            await using var reader = await lockRow.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                region = reader.GetString(0);
                tenantDomain = reader.GetString(1);
            }
        }

        if (region is null || tenantDomain is null)
        {
            LogCellMissing(cellId);
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var held = await ScalarAsync<long>(
            connection, transaction, cellId, ids, hosts, hostTenants,
            """
            SELECT (SELECT count(*) FROM directory.tenants WHERE id = ANY(@ids) AND cell_id <> @cell)
                 + (SELECT count(*) FROM directory.hosts WHERE host = ANY(@hosts) AND cell_id <> @cell)
            """,
            cancellationToken);
        if (held > 0)
        {
            LogOtherCell(cellId, held);
        }

        // The hosts go before the tenants they point to: the foreign keys restrict.
        await ExecuteAsync(
            connection, transaction, region, cellId, ids, hosts, hostTenants,
            "DELETE FROM directory.hosts WHERE cell_id = @cell AND NOT ((host, tenant_id) IN (SELECT * FROM unnest(@hosts, @hostTenants)))",
            cancellationToken);
        var removed = await IdsAsync(
            connection, transaction, region, cellId, ids, hosts, hostTenants,
            "DELETE FROM directory.tenants WHERE cell_id = @cell AND NOT (id = ANY(@ids)) RETURNING id",
            cancellationToken);
        var added = await IdsAsync(
            connection, transaction, region, cellId, ids, hosts, hostTenants,
            "INSERT INTO directory.tenants (id, region_code, cell_id) SELECT unnest(@ids), @region, @cell ON CONFLICT (id) DO NOTHING RETURNING id",
            cancellationToken);
        await ExecuteAsync(
            connection, transaction, region, cellId, ids, hosts, hostTenants,
            """
            INSERT INTO directory.hosts (host, tenant_id, cell_id)
            SELECT x.h, x.t, @cell FROM unnest(@hosts, @hostTenants) AS x(h, t)
            JOIN directory.tenants dt ON dt.id = x.t AND dt.cell_id = @cell
            ON CONFLICT (host) DO NOTHING
            """,
            cancellationToken);
        await ExecuteAsync(
            connection, transaction, region, cellId, ids, hosts, hostTenants,
            "UPDATE directory.cells SET tenant_count = @count WHERE id = @cell",
            cancellationToken,
            ids.Length);

        await transaction.CommitAsync(cancellationToken);

        // After the commit: the queue is in another database, and a job that ran first would find the old rows.
        foreach (var id in removed)
        {
            await TenantRecordJobs.EnqueueRemoveAsync(controlPlane, id, tenantDomain, cancellationToken);
        }

        foreach (var id in added)
        {
            await TenantRecordJobs.EnqueueWriteAsync(controlPlane, id, cancellationToken);
        }
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string cellId, string[] ids, string[] hosts, string[] hostTenants, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql, null, cellId, ids, hosts, hostTenants, null);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<string[]> IdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string region,
        string cellId,
        string[] ids,
        string[] hosts,
        string[] hostTenants,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql, region, cellId, ids, hosts, hostTenants, null);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return [.. result];
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string region,
        string cellId,
        string[] ids,
        string[] hosts,
        string[] hostTenants,
        string sql,
        CancellationToken cancellationToken,
        int? count = null)
    {
        await using var command = Command(connection, transaction, sql, region, cellId, ids, hosts, hostTenants, count);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Each statement uses some of the parameters. Npgsql sends only those that its text names.
    private static NpgsqlCommand Command(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, string? region, string cellId, string[] ids, string[] hosts, string[] hostTenants, int? count)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("cell", cellId);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ids });
        command.Parameters.Add(new NpgsqlParameter("hosts", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = hosts });
        command.Parameters.Add(new NpgsqlParameter("hostTenants", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = hostTenants });
        if (region is not null)
        {
            command.Parameters.AddWithValue("region", region);
        }

        if (count is { } value)
        {
            command.Parameters.AddWithValue("count", value);
        }

        return command;
    }

    [LoggerMessage(LogLevel.Warning, "The cell {CellId} could not list its tenants. Its directory rows stay as they are.")]
    private partial void LogListFailed(Exception exception, string cellId);

    [LoggerMessage(LogLevel.Warning, "The cell {CellId} has no row in the directory, so its tenants are not corrected.")]
    private partial void LogCellMissing(string cellId);

    [LoggerMessage(LogLevel.Warning, "The cell {CellId} lists {Count} tenants or hosts that the directory holds under another cell. Those rows stay as they are.")]
    private partial void LogOtherCell(string cellId, long count);
}
