using FluentAssertions;

namespace Axis.Architecture.Tests;

/// <summary>
/// Guards architecture fitness tests against silently skipping a new module
/// layer because its project is missing from the architecture test references.
/// </summary>
public class ModuleAssemblyDiscoveryTests
{
    public static IEnumerable<object[]> ModuleLayerProjects() =>
        Conventions.ModuleNames
            .SelectMany(module => Conventions.LayerNames.Select(layer => new
            {
                Module = module,
                Layer = layer,
                ProjectPath = Path.Combine(
                    Conventions.RepositoryRootPath,
                    "src",
                    "Modules",
                    module,
                    $"Axis.{module}.{layer}",
                    $"Axis.{module}.{layer}.csproj"),
            }))
            .Where(project => File.Exists(project.ProjectPath))
            .Select(project => new object[] { project.Module, project.Layer, project.ProjectPath });

    [Fact]
    public void ModuleLayerProjectDiscovery_WhenRun_FindsAtLeastOneLayerProject()
    {
        ModuleLayerProjects().Should().NotBeEmpty(
            "architecture tests must discover module layer projects before checking load coverage.");
    }

    [Theory]
    [MemberData(nameof(ModuleLayerProjects))]
    public void ModuleLayerProject_WhenPresent_IsLoadableByArchitectureTests(
        string module,
        string layer,
        string projectPath)
    {
        Conventions.TryLoadModuleLayer(module, layer).Should().NotBeNull(
            $"architecture tests must reference {projectPath} so Axis.{module}.{layer} conventions are enforced.");
    }
}
