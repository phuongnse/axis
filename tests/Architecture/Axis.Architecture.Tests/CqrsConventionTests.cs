using Axis.Shared.Application.CQRS;
using FluentAssertions;
using MediatR;

namespace Axis.Architecture.Tests;

/// <summary>
/// Convention checks for CQRS request contracts declared in module Application
/// layers.
/// </summary>
public class CqrsConventionTests
{
    public static IEnumerable<object[]> ApplicationRequestTypes() =>
        Conventions.ModuleNames
            .Select(module => Conventions.TryLoadModuleLayer(module, "Application"))
            .Where(assembly => assembly is not null)
            .SelectMany(assembly => assembly!.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(ImplementsMediatRRequest)
            .Select(type => new object[] { type });

    [Fact]
    public void RequestDiscovery_WhenRunOnLoadedAssemblies_FindsAtLeastOneRequest()
    {
        ApplicationRequestTypes().Should().NotBeEmpty(
            "CQRS convention checks must run against real Application request types.");
    }

    [Theory]
    [MemberData(nameof(ApplicationRequestTypes))]
    public void Request_WhenDeclared_UsesAxisCqrsMarker(Type requestType)
    {
        ImplementsAxisCqrsMarker(requestType).Should().BeTrue(
            $"{requestType.FullName} is a MediatR request. Use Axis ICommand, ICommand<T>, " +
            "or IQuery<T> so commands and side-effect-free queries stay explicit.");
    }

    private static bool ImplementsMediatRRequest(Type type) =>
        type.GetInterfaces().Any(IsMediatRRequest);

    private static bool IsMediatRRequest(Type candidate) =>
        candidate.IsGenericType
        && candidate.GetGenericTypeDefinition() == typeof(IRequest<>);

    private static bool ImplementsAxisCqrsMarker(Type type) =>
        type.GetInterfaces().Any(IsAxisCqrsMarker);

    private static bool IsAxisCqrsMarker(Type candidate) =>
        candidate == typeof(ICommand)
        || (candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(ICommand<>)
                || candidate.GetGenericTypeDefinition() == typeof(IQuery<>)));
}
