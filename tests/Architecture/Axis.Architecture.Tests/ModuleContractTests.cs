using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Keeps optional module public contracts thin enough for cross-module use.
/// </summary>
public class ModuleContractTests
{
    private static readonly string[] BannedNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "MediatR",
        "Npgsql",
        "FluentValidation",
    ];

    public static IEnumerable<object[]> ContractAssemblies() =>
        Conventions.ModuleNames
            .Select(module => (Module: module, Assembly: Conventions.TryLoadModuleLayer(module, "Contracts")))
            .Where(contract => contract.Assembly is not null)
            .Select(contract => new object[] { contract.Module, contract.Assembly! });

    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void ModuleContracts_WhenInspected_DoNotDependOnRuntimeOrPersistencePackages(
        string module,
        Assembly contractAssembly)
    {
        TestResult result = Types.InAssembly(contractAssembly)
            .Should()
            .NotHaveDependencyOnAny(BannedNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Axis.{module}.Contracts must stay transport- and persistence-free. " +
            $"Offending types: {FormatFailingTypes(result)}");
    }

    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void ModuleContracts_WhenInspected_DoNotDependOnOwningModuleInternals(
        string module,
        Assembly contractAssembly)
    {
        string[] forbiddenNamespaces =
        [
            $"Axis.{module}.Domain",
            $"Axis.{module}.Application",
            $"Axis.{module}.Infrastructure",
        ];

        TestResult result = Types.InAssembly(contractAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Axis.{module}.Contracts must not depend on owning module internals. " +
            $"Offending types: {FormatFailingTypes(result)}");
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
