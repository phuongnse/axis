namespace Axis.ControlPlane.RegionDirectory;

/// <summary>A row of <c>directory.hosts</c>: a host, its tenant and its cell.</summary>
public sealed class HostRow
{
    public string Host { get; set; } = "";

    public string TenantId { get; set; } = "";

    public string CellId { get; set; } = "";
}
