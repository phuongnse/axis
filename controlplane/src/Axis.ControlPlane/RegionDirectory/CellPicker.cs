using Axis.ControlPlane.Cells;
using Npgsql;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Picks the cell for a new tenant, see <c>docs/reference/region-directory.md#picking-a-cell</c>. It takes the least full cell
/// (tenant count ÷ cap) of the region that takes new tenants and is under 80% of its cap, and the lower cell id breaks a tie.
/// The caller's create call asks that cell to create the tenant. When the cell answers that it is full, the picker marks it as not
/// taking new tenants and tries the next one. When the cell accepts, one transaction writes the tenant row and raises the cell's
/// count by one. The <see cref="DirectoryCorrection"/> may have added the row already, as the cell has the tenant once it
/// accepts. Then the write keeps that row and leaves the count, which the correction has set. The same transaction writes the tenant's
/// subdomain host row, and after it commits the picker queues the <c>writeTenantRecord</c> job on the control plane database, see
/// <see cref="TenantRecordJobs"/>. With no cell left it logs an error and returns <see cref="CellPlacement.Queued"/>.
/// </summary>
internal sealed partial class CellPicker(
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    NpgsqlDataSource controlPlane,
    ILogger<CellPicker> logger)
{
    /// <summary>
    /// Places <paramref name="tenantId"/> in a cell of <paramref name="regionCode"/>. <paramref name="create"/> receives the id of
    /// the chosen cell and asks it to create the tenant. Any exception of it other than a full answer is not caught.
    /// </summary>
    public async Task<CellPlacement> PlaceAsync(
        string regionCode, string tenantId, Func<string, CancellationToken, Task> create, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(create);

        while (true)
        {
            var cellId = await LeastFullCellAsync(regionCode, cancellationToken);
            if (cellId is null)
            {
                LogNoCell(regionCode);
                return new CellPlacement(CellPlacement.Queued, null);
            }

            try
            {
                await create(cellId, cancellationToken);
            }
            catch (CellCallException exception) when (exception.CellFull)
            {
                await MarkFullAsync(cellId, cancellationToken);
                LogCellFull(cellId);
                continue;
            }

            await WriteTenantAsync(regionCode, tenantId, cellId, cancellationToken);
            return new CellPlacement(CellPlacement.Placed, cellId);
        }
    }

    private async Task<string?> LeastFullCellAsync(string regionCode, CancellationToken cancellationToken)
    {
        // Under 80% of the cap is count < 0.8 * cap, kept in whole numbers. The ordinal collation makes the id tie-break the same
        // on every database.
        await using var command = directory.CreateCommand("""
            SELECT id FROM directory.cells
            WHERE region_code = @region AND takes_new_tenants AND tenant_count * 5 < cap * 4
            ORDER BY tenant_count::numeric / cap, id COLLATE "C"
            LIMIT 1
            """);
        command.Parameters.AddWithValue("region", regionCode);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task MarkFullAsync(string cellId, CancellationToken cancellationToken)
    {
        await using var command = directory.CreateCommand("UPDATE directory.cells SET takes_new_tenants = false WHERE id = @id");
        command.Parameters.AddWithValue("id", cellId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteTenantAsync(string regionCode, string tenantId, string cellId, CancellationToken cancellationToken)
    {
        await using var connection = await directory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        int inserted;
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO directory.tenants (id, region_code, cell_id) VALUES (@id, @region, @cell) ON CONFLICT (id) DO NOTHING", connection, transaction))
        {
            insert.Parameters.AddWithValue("id", tenantId);
            insert.Parameters.AddWithValue("region", regionCode);
            insert.Parameters.AddWithValue("cell", cellId);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (inserted == 1)
        {
            await using var count = new NpgsqlCommand(
            "UPDATE directory.cells SET tenant_count = tenant_count + 1 WHERE id = @id", connection, transaction);
            count.Parameters.AddWithValue("id", cellId);
            await count.ExecuteNonQueryAsync(cancellationToken);
        }

        // The subdomain host, so the directory's host rows match the DNS records. The cell's own list adds the other hosts.
        await using (var host = new NpgsqlCommand(
            """
            INSERT INTO directory.hosts (host, tenant_id, cell_id)
            SELECT lower(@id || '.' || r.tenant_domain), @id, @cell FROM directory.regions r WHERE r.code = @region
            ON CONFLICT (host) DO NOTHING
            """, connection, transaction))
        {
            host.Parameters.AddWithValue("id", tenantId);
            host.Parameters.AddWithValue("cell", cellId);
            host.Parameters.AddWithValue("region", regionCode);
            await host.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // After the commit: the queue is in another database, and a job that ran first would find no row.
        await TenantRecordJobs.EnqueueWriteAsync(controlPlane, tenantId, cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "No cell of the region {Region} takes a new tenant. Open a cell.")]
    private partial void LogNoCell(string region);

    [LoggerMessage(LogLevel.Warning, "The cell {CellId} answered that it is full and takes no new tenants.")]
    private partial void LogCellFull(string cellId);
}
