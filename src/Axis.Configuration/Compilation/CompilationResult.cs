using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The outcome of compiling an application folder. <see cref="Model"/> is present only when no
/// diagnostic is an error.
/// </summary>
public sealed record CompilationResult(ApplicationModel? Model, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
