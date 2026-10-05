using Axis.Configuration.Diagnostics;

namespace Axis.Data;

/// <summary>
/// The outcome of provisioning a compiled application: the diagnostics that stopped it, or the
/// statements that were applied. Statements are empty whenever any diagnostic exists.
/// </summary>
public sealed record ProvisioningResult(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> Statements);
