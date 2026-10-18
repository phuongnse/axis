using Axis.Configuration.Diagnostics;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Tells whether a name like <c>departments/Department</c> reaches a module that the application
/// does not use. The text before the first <c>/</c> is the module id. A module that is declared but
/// could not be pinned already has its own diagnostic on its <c>modules</c> entry, so a reference
/// under it is not reported again.
/// </summary>
internal sealed class ModuleReferences(IReadOnlySet<string> declared, IReadOnlySet<string> pinned)
{
    /// <summary>
    /// Reports <c>AXC0207</c> when <paramref name="name"/> starts with the id of a module that
    /// <c>modules</c> does not declare. Returns true when the name names a module that is not
    /// usable, whether reported here or earlier, so the caller skips its own unknown-name report.
    /// </summary>
    public bool ReportUndeclared(string name, string path, Action<string, string, string> report)
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return false;
        }

        var moduleId = name[..slash];
        if (!declared.Contains(moduleId))
        {
            report(
                ExpressionDiagnosticCodes.UndeclaredModuleReference,
                $"The name '{name}' refers to the module '{moduleId}', which the application does not declare. Add '{moduleId}' to 'modules' in application.json.",
                path);
            return true;
        }

        return !pinned.Contains(moduleId);
    }
}
