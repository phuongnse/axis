using Axis.Configuration.Diagnostics;
using Axis.Configuration.Realms;

namespace Axis.Data;

/// <summary>
/// The outcome of activating a compiled release: the diagnostics that stopped it, and the warnings of
/// a release that was activated. The release is active when none of them is an error.
/// </summary>
public sealed record ActivationResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>The realm state that the tenant's active releases want, set only after a successful activation.</summary>
    public RealmPlan? Realm { get; init; }
}
