using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

/// <summary>
/// The sample modules that the test project copies to the test output, laid out as they are installed:
/// <c>departments/1.1.0</c> and <c>department-budgets/1.0.0</c>.
/// </summary>
internal static class SampleModules
{
    private static readonly Lazy<IReadOnlyList<InstalledModule>> _modules = new(() => ModuleCatalog.Load(Path).Modules);

    /// <summary>The folder that acts as <c>Modules:Path</c>.</summary>
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "SampleModules");

    /// <summary>The installed modules, to compile the samples against.</summary>
    public static IReadOnlyList<InstalledModule> Modules => _modules.Value;
}
