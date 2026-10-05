namespace Axis.Configuration.Diagnostics;

/// <summary>The order in which diagnostics are reported: by file, then path, then code and message.</summary>
public static class DiagnosticOrder
{
    public static List<Diagnostic> Sort(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics
            .OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToList();
}
