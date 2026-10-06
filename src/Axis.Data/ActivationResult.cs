using Axis.Configuration.Diagnostics;

namespace Axis.Data;

/// <summary>
/// The outcome of activating a compiled release: the diagnostics that stopped it. The release is
/// active when there are none.
/// </summary>
public sealed record ActivationResult(IReadOnlyList<Diagnostic> Diagnostics);
