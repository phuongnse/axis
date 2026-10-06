using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Storage;

namespace Axis.Configuration.Releases;

/// <summary>
/// The outcome of compiling an application folder into a release. <see cref="Release"/> and the
/// compiled <see cref="Model"/> are present only when no diagnostic is an error.
/// </summary>
public sealed record ReleaseCompilationResult(Release? Release, ApplicationModel? Model, IReadOnlyList<Diagnostic> Diagnostics);
