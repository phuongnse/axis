using Axis.Configuration.Modules;

namespace Axis.Integration.Tests;

/// <summary>
/// The sample modules that the test project copies to the test output, laid out as they are installed:
/// <c>departments/1.1.0</c> and <c>department-budgets/1.1.0</c>. The test project's build does this, so the tests do not need
/// <c>scripts/build.sh</c> first.
/// </summary>
internal static class SampleModules
{
    private static readonly Lazy<IReadOnlyList<InstalledModule>> _modules = new(() => ModuleCatalog.Load(Path).Modules);

    /// <summary>The folder that acts as <c>Modules:Path</c>.</summary>
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "SampleModules");

    /// <summary>The installed modules, to compile the samples against.</summary>
    public static IReadOnlyList<InstalledModule> Modules => _modules.Value;

    /// <summary>The id of the Department entity in the sample module file, before it is derived for an application.</summary>
    public static readonly Guid DepartmentEntityId = Guid.Parse("3a5633d1-fc29-4997-b93d-36a1a865c8e3");
}
