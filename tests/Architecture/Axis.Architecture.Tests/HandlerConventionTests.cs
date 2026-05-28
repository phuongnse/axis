using System.Reflection;
using FluentAssertions;
using MediatR;

namespace Axis.Architecture.Tests;

/// <summary>
/// Convention checks for MediatR command/query handlers across every module's
/// Application layer.
///
/// <para>
/// Rules enforced:
/// <list type="bullet">
/// <item>Handlers must accept <c>CancellationToken</c> as the last parameter of
/// <c>Handle</c> — required so cancellations propagate from the HTTP
/// pipeline / Wolverine pipeline through to EF and HTTP/gRPC calls.</item>
/// <item>Handler classes must be <c>sealed</c> — handlers are leaf types;
/// inheritance is never the right tool for composing them.</item>
/// </list>
/// </para>
/// </summary>
public class HandlerConventionTests
{
    /// <summary>
    /// Returns every concrete class in any <c>Axis.{Module}.Application</c>
    /// assembly that implements <see cref="IRequestHandler{TRequest, TResponse}"/>
    /// (which covers both <c>ICommandHandler</c> and <c>IQueryHandler</c> via
    /// their interface chain).
    /// </summary>
    public static IEnumerable<object[]> AllRequestHandlers() =>
        Conventions.ModuleNames
            .Select(m => Conventions.TryLoadModuleLayer(m, "Application"))
            .Where(a => a is not null)
            .SelectMany(a => a!.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(ImplementsRequestHandler)
            .Select(t => new object[] { t });

    [Fact]
    public void HandlerDiscovery_WhenRunOnLoadedAssemblies_FindsAtLeastOneHandler()
    {
        // Guard against the silent-skip failure mode: if Application assemblies
        // fail to load, AllRequestHandlers returns empty and the Theory tests
        // below produce zero cases (xUnit reports "Passed" with no warnings).
        AllRequestHandlers().Should().NotBeEmpty(
            "Handler convention checks must run against real handlers — empty discovery means " +
            "Application assemblies aren't being loaded and convention enforcement is silently off.");
    }

    [Theory]
    [MemberData(nameof(AllRequestHandlers))]
    public void Handler_WhenInvoked_AcceptsCancellationTokenAsLastParameter(Type handlerType)
    {
        MethodInfo handle = FindHandleMethod(handlerType);
        ParameterInfo[] parameters = handle.GetParameters();

        parameters.Should().NotBeEmpty(
            $"{handlerType.FullName}.Handle must declare parameters.");

        ParameterInfo last = parameters[^1];
        last.ParameterType.Should().Be<CancellationToken>(
            $"{handlerType.FullName}.Handle must accept CancellationToken as its LAST parameter " +
            "so cancellations propagate from HTTP/Wolverine through to EF + outbound calls. " +
            $"Got: {string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))}.");
    }

    [Theory]
    [MemberData(nameof(AllRequestHandlers))]
    public void Handler_WhenDeclared_IsSealed(Type handlerType)
    {
        handlerType.IsSealed.Should().BeTrue(
            $"{handlerType.FullName} is a MediatR handler — leaf type. Mark it 'sealed' so future " +
            "agents don't introduce a handler base class hierarchy (composition > inheritance for handlers).");
    }

    private static bool ImplementsRequestHandler(Type type) =>
        type.GetInterfaces().Any(IsRequestHandlerInterface);

    private static bool IsRequestHandlerInterface(Type i) =>
        i.IsGenericType
        && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
            || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>));

    private static MethodInfo FindHandleMethod(Type handlerType)
    {
        // A type may implement multiple IRequestHandler closes (rare) — prefer the
        // public instance Handle. Excludes the interface map's accessor.
        MethodInfo? handle = handlerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == "Handle");

        if (handle is not null)
            return handle;

        // Fallback: walk the interface map (handler implements Handle as private).
        InterfaceMapping map = handlerType
            .GetInterfaces()
            .Where(IsRequestHandlerInterface)
            .Select(handlerType.GetInterfaceMap)
            .First();
        return map.TargetMethods.First(m => m.Name.EndsWith("Handle", StringComparison.Ordinal));
    }
}
