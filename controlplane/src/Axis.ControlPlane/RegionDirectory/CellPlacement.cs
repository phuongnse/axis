namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Where a new tenant went: <see cref="Placed"/> with the id of its cell, or <see cref="Queued"/> with no cell when none of the
/// region's cells has room.
/// </summary>
internal sealed record CellPlacement(string State, string? CellId)
{
    public const string Placed = "placed";

    public const string Queued = "queued";
}
