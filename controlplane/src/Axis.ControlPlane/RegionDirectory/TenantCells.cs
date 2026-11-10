using Axis.ControlPlane.Cells;
using Npgsql;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>Finds the cell that holds a tenant, from the region directory, see <c>docs/reference/region-directory.md#routing-a-host</c>.</summary>
internal static class TenantCells
{
    /// <summary>The id of the cell that the directory holds for <paramref name="tenantId"/>. A tenant with no row has none, and the result is null.</summary>
    public static async Task<string?> FindCellIdAsync(NpgsqlDataSource directory, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        await using var command = directory.CreateCommand("SELECT cell_id FROM directory.tenants WHERE id = @id");
        command.Parameters.AddWithValue("id", tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    /// <summary>The client of the cell <paramref name="cellId"/>, or null when it is null or not one of the region's cells.</summary>
    public static CellClient? Client(CellClients cells, string? cellId)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return cellId is null ? null : cells.All.FirstOrDefault(client => string.Equals(client.CellId, cellId, StringComparison.Ordinal));
    }
}
