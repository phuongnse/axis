using Axis.Configuration.Diagnostics;

namespace Axis.Data;

/// <summary>
/// The outcome of activating a compiled release: the diagnostics that stopped it, and the warnings of
/// a release that was activated. The release is active when none of them is an error.
/// </summary>
public sealed record ActivationResult(IReadOnlyList<Diagnostic> Diagnostics);
