namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// A row of <c>directory.held</c>: a tenant id or a host that is not given out again. <see cref="Kind"/> is
/// <c>tenant</c> or <c>host</c>. A null <see cref="HeldUntil"/> holds the value forever.
/// </summary>
public sealed class HeldRow
{
    public string Kind { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTimeOffset? HeldUntil { get; set; }
}
