using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

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

    [Fact]
    public void Identity_WhenInspected_ReferencesOnlyOwnedAndSharedContractAssemblies()
    {
        IReadOnlyDictionary<string, string[]> allowedAxisReferences =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Contracts"] = [],
                ["Domain"] = ["Axis.Shared.Domain"],
                ["Application"] =
                [
                    "Axis.Audit.Contracts",
                    "Axis.Identity.Contracts",
                    "Axis.Identity.Domain",
                    "Axis.Shared.Application",
                    "Axis.Shared.Domain",
                ],
                ["Infrastructure"] =
                [
                    "Axis.Audit.Contracts",
                    "Axis.Identity.Application",
                    "Axis.Identity.Contracts",
                    "Axis.Identity.Domain",
                    "Axis.Shared.Application",
                    "Axis.Shared.Domain",
                    "Axis.Shared.Infrastructure",
                ],
            };

        foreach (string layer in Conventions.LayerNames)
        {
            Assembly? identityAssembly = Conventions.TryLoadModuleLayer("Identity", layer);
            if (identityAssembly is null)
                continue;

            identityAssembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name?.StartsWith("Axis.", StringComparison.Ordinal) is true)
                .Should().OnlyContain(name =>
                    allowedAxisReferences[layer].Contains(name, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void SolutionsApplication_WhenInspected_ReferencesOnlyOwnedAndPublicContractAssemblies()
    {
        Assembly application = Conventions.TryLoad("Axis.Solutions.Application")
            ?? throw new InvalidOperationException("Axis.Solutions.Application could not be loaded.");
        string[] allowedAxisReferences =
        [
            "Axis.Audit.Contracts",
            "Axis.Authorization.Contracts",
            "Axis.BusinessObjects.Contracts",
            "Axis.Rules.Contracts",
            "Axis.Shared.Application",
            "Axis.Shared.Domain",
            "Axis.Solutions.Contracts",
            "Axis.Solutions.Domain",
        ];

        application.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("Axis.", StringComparison.Ordinal) is true)
            .Should().OnlyContain(name => allowedAxisReferences.Contains(name, StringComparer.Ordinal));

        foreach (string consumer in new[] { "Authorization", "BusinessObjects", "Rules" })
        {
            AssertAssemblyHasNoDependencyOn(application, $"Axis.{consumer}.Domain");
            AssertAssemblyHasNoDependencyOn(application, $"Axis.{consumer}.Application");
            AssertAssemblyHasNoDependencyOn(application, $"Axis.{consumer}.Infrastructure");
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

    private static void AssertAssemblyHasNoDependencyOn(Assembly assembly, string forbiddenNamespacePrefix)
    {
        TestResult result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOn(forbiddenNamespacePrefix)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"{assembly.GetName().Name} must not depend on {forbiddenNamespacePrefix}. " +
            $"Failing types: {FormatFailingTypes(result)}.");
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
