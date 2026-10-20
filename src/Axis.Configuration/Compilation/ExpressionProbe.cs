using Axis.Expressions.Diagnostics;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Watches one expression place during a compile: the file and the JSON Pointer of one expression
/// string. When the compile checks an expression there, the probe keeps what it saw, the type or the
/// problem, so the check of an expression is the compile's own and not a copy of its scope rules.
/// </summary>
public sealed class ExpressionProbe(string file, string path)
{
    public string File { get; } = file;

    public string Path { get; } = path;

    /// <summary>Whether the compile checked an expression at the file and path.</summary>
    public bool Found { get; private set; }

    /// <summary>The expression's type, set only when the check found no problem.</summary>
    public ExpressionType? Type { get; private set; }

    /// <summary>The first problem the check found, in parsing or in type checking.</summary>
    public ExpressionDiagnostic? Diagnostic { get; private set; }

    /// <summary>Keeps the first record at the file and path, which both match ordinally.</summary>
    internal void Record(string file, string path, ExpressionType? type, ExpressionDiagnostic? diagnostic)
    {
        if (Found || !string.Equals(file, File, StringComparison.Ordinal) || !string.Equals(path, Path, StringComparison.Ordinal))
        {
            return;
        }

        Found = true;
        Diagnostic = diagnostic;
        Type = diagnostic is null ? type : null;
    }
}
