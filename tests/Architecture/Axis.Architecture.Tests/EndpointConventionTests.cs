using System.Reflection;
using FluentAssertions;

namespace Axis.Architecture.Tests;

/// <summary>
/// Convention checks for Minimal API endpoint classes in <c>Axis.Api.Endpoints</c>.
///
/// <para>
/// Rules enforced:
/// <list type="bullet">
/// <item><b>Naming</b> — any static class that hosts a <c>Map*</c> extension method
/// returning <c>IEndpointRouteBuilder</c> must be named <c>*Endpoints</c>.
/// Without the suffix, `Program.cs` registration calls (`app.MapXxx()`)
/// lose grep-ability.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Authorization presence</b> (every endpoint chains <c>.RequireAuthorization()</c>
/// unless deliberately anonymous) is NOT checked here — it requires walking
/// the <c>EndpointDataSource</c> at runtime via a <c>WebApplicationFactory</c>.
/// That check ships in PR #97 along with the DI registration scanner that
/// shares the same factory infrastructure.
/// </para>
/// </summary>
public class EndpointConventionTests
{
    public static IEnumerable<object[]> AllEndpointHostClasses()
    {
        Assembly api = Conventions.TryLoad("Axis.Api")
            ?? throw new InvalidOperationException(
                "Axis.Api assembly could not be loaded; endpoint convention checks were not executed. " +
                "Add the missing ProjectReference to Axis.Architecture.Tests.csproj.");

        IEnumerable<Type> hosts = api.GetTypes()
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed) // static = abstract + sealed
            .Where(HasEndpointRegistrationMethod);

        foreach (Type t in hosts)
            yield return new object[] { t };
    }

    [Fact]
    public void EndpointHostDiscovery_WhenRunOnAxisApi_FindsAtLeastOneHost()
    {
        // Guard against the silent-skip failure mode (see sibling tests).
        AllEndpointHostClasses().Should().NotBeEmpty(
            "Endpoint convention checks must run against real endpoint host classes — empty " +
            "discovery means MapXxx methods aren't recognised and convention enforcement is silently off.");
    }

    [Theory]
    [MemberData(nameof(AllEndpointHostClasses))]
    public void EndpointHostClass_WhenInspected_HasNameEndingWithEndpoints(Type endpointHost)
    {
        endpointHost.Name.Should().EndWith("Endpoints",
            $"{endpointHost.FullName} hosts Map* extension methods returning IEndpointRouteBuilder " +
            "but its class name does not end in 'Endpoints'. Rename so `app.MapXxx()` calls in " +
            "Program.cs are grep-able back to a single class.");
    }

    private static bool HasEndpointRegistrationMethod(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m =>
                m.Name.StartsWith("Map", StringComparison.Ordinal)
                && m.ReturnType.Name == "IEndpointRouteBuilder");
}
