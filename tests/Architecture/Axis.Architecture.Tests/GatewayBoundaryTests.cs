using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces CLAUDE.md P0 at the REST gateway: <c>Axis.Api</c> must not take
/// in-process dependencies on another module's Application layer (repositories,
/// services). Endpoints delegate via <c>ISender</c>; cross-module sync uses gRPC
/// from consuming modules' Infrastructure (ADR-014).
/// </summary>
public class GatewayBoundaryTests
{
    private const string ApplicationRepositoriesSuffix = ".Application.Repositories";

    private const string ApplicationServicesSuffix = ".Application.Services";

    [Fact]
    public void ApiEndpoints_MustNotDependOnOtherModulesApplicationRepositories()
    {
        Assembly? api = Conventions.TryLoad("Axis.Api");
        api.Should().NotBeNull("Axis.Api assembly must be referenced by architecture tests.");

        IEnumerable<Type> endpointTypes = api!.GetTypes()
            .Where(t => t.IsClass && t.Namespace == "Axis.Api.Endpoints");

        foreach (Type endpointType in endpointTypes)
        {
            TestResult result = Types.InAssembly(api)
                .That()
                .HaveName(endpointType.Name)
                .Should()
                .NotHaveDependencyOnAny(
                    Conventions.ModuleNames
                        .Select(m => $"Axis.{m}{ApplicationRepositoriesSuffix}")
                        .ToArray())
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"{endpointType.FullName} must not depend on another module's Application.Repositories — " +
                "use MediatR commands/queries in the owning module instead.");
        }
    }

    [Fact]
    public void ApiInfrastructure_MustNotDependOnOtherModulesApplicationLayer()
    {
        Assembly? api = Conventions.TryLoad("Axis.Api");
        api.Should().NotBeNull();

        string[] forbiddenPrefixes = Conventions.ModuleNames
            .SelectMany(m => new[]
            {
                $"Axis.{m}{ApplicationRepositoriesSuffix}",
                $"Axis.{m}{ApplicationServicesSuffix}",
            })
            .ToArray();

        IEnumerable<Type> infraTypes = api!.GetTypes()
            .Where(t => t.IsClass && t.Namespace?.StartsWith("Axis.Api.Infrastructure", StringComparison.Ordinal) == true);

        foreach (Type infraType in infraTypes)
        {
            TestResult result = Types.InAssembly(api)
                .That()
                .HaveName(infraType.Name)
                .Should()
                .NotHaveDependencyOnAny(forbiddenPrefixes)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"{infraType.FullName} must not depend on another module's Application layer — " +
                "use gRPC contracts (Axis.*.Contracts) from the consuming module's Infrastructure.");
        }
    }
}
