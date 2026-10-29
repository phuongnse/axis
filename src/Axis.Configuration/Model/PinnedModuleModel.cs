using System.Collections.ObjectModel;

namespace Axis.Configuration.Model;

/// <summary>
/// A module an application uses: its id, the version range as written, and the exact installed
/// version that compilation picked from that range.
/// </summary>
public sealed record PinnedModuleModel(string Id, string Range, string Version)
{
    /// <summary>The role slots of the pinned version, each mapped to the name of an application role.</summary>
    public IReadOnlyDictionary<string, string> Roles { get; init; } = ReadOnlyDictionary<string, string>.Empty;
}
