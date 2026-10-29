using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Tells whether a name like <c>departments/Department</c> reaches a module that the application
/// does not use. The text before the first <c>/</c> is the module id. A module that is declared but
/// could not be pinned already has its own diagnostic on its <c>modules</c> entry, so a reference
/// under it is not reported again. The application declares only the modules it lists itself. A
/// resource of a module may name every pinned module, because its own module is pinned even when
/// only another module depends on it, see <see cref="For"/>.
/// </summary>
internal sealed class ModuleReferences(IReadOnlySet<string> declared, IReadOnlySet<string> pinned)
{
    private ModuleReferences? _forModules;

    /// <summary>
    /// The references to check <paramref name="resource"/> with: this one for an application resource,
    /// and one that treats every pinned module as declared for a module resource.
    /// </summary>
    public ModuleReferences For(Resource resource) =>
        ApplicationCompiler.IsModuleResource(resource) ? _forModules ??= new ModuleReferences(pinned, pinned) : this;

    /// <summary>
    /// Looks <paramref name="name"/> up with <paramref name="find"/>, except a name under a module that
    /// is pinned but not declared, which finds nothing. The application cannot reach a module that
    /// only another module depends on, and the caller then reports it with <see cref="ReportUndeclared"/>.
    /// </summary>
    public T? Find<T>(string name, Func<string, T?> find)
        where T : class
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);
        return slash >= 0 && !declared.Contains(name[..slash]) && pinned.Contains(name[..slash]) ? null : find(name);
    }

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
