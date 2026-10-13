namespace Axis.Configuration.Model;

/// <summary>
/// An extension package an application uses: its id, the version range as written, and the exact
/// installed version that compilation picked from that range.
/// </summary>
public sealed record PinnedPackageModel(string Id, string Range, string Version);
