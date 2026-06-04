using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces CLAUDE.md P0 rules:
/// <list type="bullet">
/// <item>No project reference from <c>Axis.{ModuleA}.*</c> to <c>Axis.{ModuleB}.*</c>
/// except to <c>Axis.{ModuleB}.Contracts</c>.</item>
/// <item>No in-process method call into another module's Application or Infrastructure.</item>
/// </list>
/// This is the architectural lock that makes "extraction is a redeploy" true ([ADR-010](../../docs/TECH_STACK.md)).
/// </summary>
public class ModuleBoundaryTests
{
    /// <summary>
    /// Allowed cross-module layer — only Contracts may be referenced across module boundaries.
    /// </summary>
    private const string ContractsLayer = "Contracts";

    /// <summary>
    /// Known cross-module boundary violations that pre-date the architecture
    /// tests. Each entry MUST have a matching entry in <c>docs/WORKAROUNDS.md</c>
    /// with an explicit cleanup trigger. Remove from this dictionary when the
    /// workaround is resolved — that turns the test into a regression guard
    /// (any NEW violation of the same pair fails the test).
    ///
    /// <para>
    /// Key: <c>(sourceModule, sourceLayer, targetModule, targetLayer)</c>.<br/>
    /// Value: set of fully-qualified type names allowed to make the reference.
    /// </para>
    /// </summary>
    private static readonly Dictionary<(string SourceModule, string SourceLayer, string TargetModule, string TargetLayer), HashSet<string>>
        KnownBoundaryWorkarounds = new();

    public static IEnumerable<object[]> CrossModulePairs() =>
        from a in Conventions.ModuleNames
        from b in Conventions.ModuleNames
        where a != b
        select new object[] { a, b };

    [Theory]
    [MemberData(nameof(CrossModulePairs))]
    public void Module_WhenInspected_DoesNotReferenceOtherModuleDomain(string moduleA, string moduleB)
    {
        AssertNoCrossModuleDependency(moduleA, moduleB, layer: "Domain");
    }

    [Theory]
    [MemberData(nameof(CrossModulePairs))]
    public void Module_WhenInspected_DoesNotReferenceOtherModuleApplication(string moduleA, string moduleB)
    {
        AssertNoCrossModuleDependency(moduleA, moduleB, layer: "Application");
    }

    [Theory]
    [MemberData(nameof(CrossModulePairs))]
    public void Module_WhenInspected_DoesNotReferenceOtherModuleInfrastructure(string moduleA, string moduleB)
    {
        AssertNoCrossModuleDependency(moduleA, moduleB, layer: "Infrastructure");
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

            HashSet<string> failingTypes = (result.FailingTypeNames ?? [])
                .Where(name => !string.IsNullOrEmpty(name))
                .ToHashSet();

            HashSet<string> allowed = KnownBoundaryWorkarounds.TryGetValue(
                (moduleA, aLayer, moduleB, layer), out HashSet<string>? set)
                ? set
                : [];

            HashSet<string> unexpected = failingTypes.Except(allowed).ToHashSet();
            HashSet<string> noLongerNeeded = allowed.Except(failingTypes).ToHashSet();

            unexpected.Should().BeEmpty(
                $"Axis.{moduleA}.{aLayer} depends on {forbiddenNamespacePrefix} via NEW types " +
                $"not in the WORKAROUNDS allow-list — cross-module references are only allowed " +
                $"to Axis.{moduleB}.{ContractsLayer} (CLAUDE.md P0). " +
                $"Unexpected types: {string.Join(", ", unexpected)}. " +
                "If this is intentional, add the type name to KnownBoundaryWorkarounds in " +
                "ModuleBoundaryTests.cs AND record the workaround in docs/WORKAROUNDS.md.");

            noLongerNeeded.Should().BeEmpty(
                $"Allow-list entry for ({moduleA}.{aLayer} → {moduleB}.{layer}) contains types " +
                $"that no longer reference the target — congratulations on the cleanup! Now " +
                $"remove these from KnownBoundaryWorkarounds and update docs/WORKAROUNDS.md " +
                $"to move the entry to the 'Resolved' section. Stale entries: " +
                $"{string.Join(", ", noLongerNeeded)}.");
        }
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
