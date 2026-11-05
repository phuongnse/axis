namespace Axis.ControlPlane.RegionDirectory;

/// <summary>A row of <c>directory.regions</c>: a region, its tenant domain and its control plane's address.</summary>
public sealed class RegionRow
{
    /// <summary>The region code, such as <c>vn</c>.</summary>
    public string Code { get; set; } = "";

    public string TenantDomain { get; set; } = "";

    /// <summary>The address of the region's control plane.</summary>
    public string ApiUrl { get; set; } = "";
}
