namespace Axis.Configuration.Model;

/// <summary>
/// A module an application uses: its id, the version range as written, and the exact installed
/// version that compilation picked from that range.
/// </summary>
public sealed record PinnedModuleModel(string Id, string Range, string Version);
