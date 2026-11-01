using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The outcome of compiling an application folder. <see cref="Model"/>, <see cref="ContentHash"/>
/// and <see cref="Resources"/> are present only when no diagnostic is an error. <see cref="References"/>,
/// <see cref="Names"/> and <see cref="PolicyMatrix"/> are always present, and are not stored in the release.
/// </summary>
public sealed record CompilationResult(ApplicationModel? Model, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>The reference index of the references the compile resolved, even when it has errors.</summary>
    public ReferenceIndex References { get; init; } = ReferenceIndex.Empty;

    /// <summary>The names of the loaded resources, even when the compile has errors. A file left out for its own errors is absent.</summary>
    public DefinedNames Names { get; init; } = DefinedNames.Empty;

    /// <summary>The policy matrix of the loaded roles and policies, even when the compile has errors. A file left out for its own errors is absent.</summary>
    public PolicyMatrix PolicyMatrix { get; init; } = PolicyMatrix.Empty;

    /// <summary>The modules the application pins, each with the installed version the compile picked, even when it has errors.</summary>
    public IReadOnlyList<PinnedModuleModel> ModulePins { get; init; } = [];

    /// <summary>
    /// The scenarios of the pinned modules, present when no diagnostic is an error. Each is named
    /// <c>&lt;module id&gt;/&lt;name&gt;</c> and its users, assignees and recipients hold the application roles the pins map
    /// the module's role slots to. They come in pin order and then by file path, to run after the application's own scenarios.
    /// They are not part of the release or its content hash.
    /// </summary>
    public IReadOnlyList<ScenarioResource> ModuleScenarios { get; init; } = [];

    /// <summary>The application's content hash; see <see cref="Releases.ContentHash"/>.</summary>
    public string? ContentHash { get; init; }

    /// <summary>The canonical content of every resource file, in path order.</summary>
    public IReadOnlyList<ResourceContent> Resources { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
