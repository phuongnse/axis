namespace Axis.ControlPlane.RegionDirectory;

/// <summary>A row of <c>directory.tenants</c>: a tenant, its region and its cell. The id is unique across every region.</summary>
public sealed class TenantRow
{
    public string Id { get; set; } = "";

    public string RegionCode { get; set; } = "";

    public string CellId { get; set; } = "";
}
