using Axis.Configuration.Diagnostics;

namespace Axis.Data;

/// <summary>
/// The outcome of provisioning a compiled application: the diagnostics that stopped it, or the
/// statements that were applied. Statements are empty whenever any diagnostic exists.
/// </summary>
public sealed record ProvisioningResult(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> Statements)
{
    /// <summary>
    /// Whether the active release was written in the provisioning transaction, which happens when a
    /// migration was pending.
    /// </summary>
    public bool ActiveReleaseSet { get; init; }
}
