using System.Reflection;

namespace Axis.Architecture.Tests;

/// <summary>
/// Central knowledge of the Axis assembly layout. Architecture fitness tests use
/// this to discover modules and load assemblies. Module names are read from
/// <c>src/Modules/</c> at test run time — adding a module folder is enough; no
/// edit to a string list is required (project references in the test csproj are
/// still needed for assemblies to load).
/// </summary>
internal static class Conventions
{
    /// <summary>
    /// Modules under <c>src/Modules/</c>, discovered from the repository layout.
    /// </summary>
    public static string[] ModuleNames => DiscoverModuleNames();

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

    private static string[] DiscoverModuleNames()
    {
        string modulesDir = Path.Combine(RepositoryRoot, "src", "Modules");
        if (!Directory.Exists(modulesDir))
        {
            return [];
        }

        return Directory.GetDirectories(modulesDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;
    }

    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Axis.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate repository root (Axis.sln) from test bin directory.");
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
