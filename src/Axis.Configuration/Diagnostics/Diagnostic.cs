namespace Axis.Configuration.Diagnostics;

/// <summary>
/// A configuration problem. <see cref="File"/> is relative to the application folder and uses
/// <c>/</c> separators; it is empty when the problem concerns the whole application folder.
/// <see cref="Path"/> is an RFC 6901 JSON Pointer into that file; it is empty when the problem
/// concerns the whole file or the whole folder. Messages never contain absolute paths or exception
/// text, so they can be shown to application authors.
/// </summary>
public sealed record Diagnostic(
    string Code,
    string Message,
    string File,
    string Path,
    Guid? ResourceId = null,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error);
