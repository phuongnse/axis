using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces module isolation:
/// <list type="bullet">
/// <item>No project reference from <c>Axis.{ModuleA}.*</c> to another module's internals.</item>
/// <item>No in-process method call into another module's Application or Infrastructure.</item>
/// </list>
/// </summary>
public class ModuleBoundaryTests
{
    private static IEnumerable<(string ModuleA, string ModuleB)> CrossModulePairs() =>
        from a in Conventions.ModuleNames
        from b in Conventions.ModuleNames
        where a != b
        select (a, b);

    [Fact]
    public void Modules_WhenInspected_DoNotReferenceOtherModuleDomain()
    {
        foreach ((string moduleA, string moduleB) in CrossModulePairs())
        {
            AssertNoCrossModuleDependency(moduleA, moduleB, layer: "Domain");
        }
    }

    [Fact]
    public void Modules_WhenInspected_DoNotReferenceOtherModuleApplication()
    {
        foreach ((string moduleA, string moduleB) in CrossModulePairs())
        {
            AssertNoCrossModuleDependency(moduleA, moduleB, layer: "Application");
        }
    }

    [Fact]
    public void Modules_WhenInspected_DoNotReferenceOtherModuleInfrastructure()
    {
        foreach ((string moduleA, string moduleB) in CrossModulePairs())
        {
            AssertNoCrossModuleDependency(moduleA, moduleB, layer: "Infrastructure");
        }
    }

    private static void AssertNoCrossModuleDependency(string moduleA, string moduleB, string layer)
    {
        string forbiddenNamespacePrefix = $"Axis.{moduleB}.{layer}";

        foreach (string aLayer in Conventions.LayerNames)
        {
            Assembly? aAssembly = Conventions.TryLoadModuleLayer(moduleA, aLayer);
            if (aAssembly is null)
                continue;

            TestResult result = Types.InAssembly(aAssembly)
                .Should()
                .NotHaveDependencyOn(forbiddenNamespacePrefix)
                .GetResult();

            if (result.IsSuccessful)
                continue;

            result.IsSuccessful.Should().BeTrue(
                $"Axis.{moduleA}.{aLayer} must not depend on {forbiddenNamespacePrefix}. " +
                $"Failing types: {FormatFailingTypes(result)}.");
        }
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
