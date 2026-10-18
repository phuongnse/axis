using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The outcome of compiling an application folder. <see cref="Model"/>, <see cref="ContentHash"/>
/// and <see cref="Resources"/> are present only when no diagnostic is an error. <see cref="References"/>
/// is always present, and is not stored in the release.
/// </summary>
public sealed record CompilationResult(ApplicationModel? Model, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>The reference index of the references the compile resolved, even when it has errors.</summary>
    public ReferenceIndex References { get; init; } = ReferenceIndex.Empty;

    /// <summary>The application's content hash; see <see cref="Releases.ContentHash"/>.</summary>
    public string? ContentHash { get; init; }

    /// <summary>The canonical content of every resource file, in path order.</summary>
    public IReadOnlyList<ResourceContent> Resources { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
