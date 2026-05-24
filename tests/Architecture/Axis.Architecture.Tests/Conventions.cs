using System.Reflection;

namespace Axis.Architecture.Tests;

/// <summary>
/// Central knowledge of the Axis assembly layout. Architecture fitness tests use
/// this to discover modules and load assemblies. When a new module is added to
/// the solution, the only change needed here is to add its name to
/// <see cref="ModuleNames"/> (or rely on dynamic discovery if it follows the
/// <c>Axis.{Module}.{Layer}.dll</c> convention).
/// </summary>
internal static class Conventions
{
    /// <summary>
    /// Modules that have at least one layer in the codebase. Sourced from the
    /// solution layout (<c>src/Modules/&lt;Module&gt;/...</c>). Add new modules here.
    /// </summary>
    public static readonly string[] ModuleNames =
    [
        "Identity",
        "DataModeling",
        "WorkflowBuilder",
        "FormBuilder",
        "WorkflowEngine",
        "PageBuilder",
    ];

    /// <summary>
    /// Per-module layer names. Each (module, layer) pair maps to assembly
    /// <c>Axis.{Module}.{Layer}</c>. Not every module has every layer (e.g.
    /// PageBuilder currently has no Contracts) — <see cref="TryLoad"/> handles
    /// the absence gracefully.
    /// </summary>
    public static readonly string[] LayerNames =
    [
        "Contracts",
        "Domain",
        "Application",
        "Infrastructure",
    ];

    /// <summary>
    /// Returns every <c>Axis.*.dll</c> that ships into this test project's bin
    /// folder (one per referenced project in our csproj). Use this when a test
    /// needs to scan "all Axis production assemblies" without enumerating each.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadAllAxisAssemblies()
    {
        string binDir = Path.GetDirectoryName(typeof(Conventions).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot resolve bin directory.");

        return Directory.GetFiles(binDir, "Axis.*.dll")
            .Where(path => !path.EndsWith(".Tests.dll", StringComparison.OrdinalIgnoreCase))
            .Select(TryLoadFrom)
            .OfType<Assembly>()
            .ToList();
    }

    /// <summary>Loads <c>Axis.{module}.{layer}</c> if its DLL exists; otherwise <c>null</c>.</summary>
    public static Assembly? TryLoadModuleLayer(string module, string layer) =>
        TryLoad($"Axis.{module}.{layer}");

    /// <summary>Loads an assembly by simple name; returns <c>null</c> on any failure.</summary>
    public static Assembly? TryLoad(string assemblyName)
    {
        try
        {
            return Assembly.Load(assemblyName);
        }
        catch
        {
            return null;
        }
    }

    private static Assembly? TryLoadFrom(string path)
    {
        try
        {
            return Assembly.LoadFrom(path);
        }
        catch
        {
            return null;
        }
    }
}
