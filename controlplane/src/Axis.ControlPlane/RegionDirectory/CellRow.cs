namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// A row of <c>directory.cells</c>: a cell of a region with its cap, its tenant count and whether it takes new tenants.
/// </summary>
public sealed class CellRow
{
    /// <summary>The cell id, such as <c>vn-1</c>.</summary>
    public string Id { get; set; } = "";

    public string RegionCode { get; set; } = "";

    public string InternalUrl { get; set; } = "";

    public int Cap { get; set; }

    public int TenantCount { get; set; }

    public bool TakesNewTenants { get; set; } = true;
}
