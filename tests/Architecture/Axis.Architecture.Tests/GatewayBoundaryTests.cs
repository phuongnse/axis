using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces the REST gateway boundary: endpoints delegate cross-module work through <c>ISender</c>.
/// </summary>
public class GatewayBoundaryTests
{
    private const string ApplicationRepositoriesSuffix = ".Application.Repositories";

    private const string ApplicationServicesSuffix = ".Application.Services";

    [Fact]
    public void ApiEndpoints_WhenInspected_DoNotDependOnOtherModulesApplicationRepositories()
    {
        Assembly? api = Conventions.TryLoad("Axis.Api");
        api.Should().NotBeNull("Axis.Api assembly must be referenced by architecture tests.");

        IEnumerable<Type> endpointTypes = api!.GetTypes()
            .Where(t =>
                t.IsClass &&
                t.Namespace?.StartsWith("Axis.Api.Endpoints", StringComparison.Ordinal) == true);

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
    public void ApiInfrastructure_WhenInspected_DoesNotDependOnOtherModulesApplicationLayer()
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
                $"{infraType.FullName} must not depend on another module's Application layer.");
        }
    }
}
