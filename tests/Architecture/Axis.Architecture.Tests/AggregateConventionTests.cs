using System.Reflection;
using System.Runtime.CompilerServices;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Architecture.Tests;

/// <summary>
/// Convention checks for aggregate roots across every module's Domain layer.
///
/// <para>
/// Rules enforced:
/// <list type="bullet">
/// <item>No public mutable setters — state changes go through behavior methods,
/// not property assignment. (Init-only setters are allowed; EF Core
/// needs <c>private set;</c> or <c>init;</c> for materialisation.)</item>
/// <item>Aggregates expose at least one public/internal factory method (a
/// <c>Create</c> static or equivalent). The check is best-effort: it
/// flags aggregates whose only public constructor is parameterless,
/// which usually means a missing factory.</item>
/// </list>
/// </para>
/// </summary>
public class AggregateConventionTests
{
    public static IEnumerable<object[]> AllAggregateRoots() =>
        Conventions.ModuleNames
            .Select(m => Conventions.TryLoadModuleLayer(m, "Domain"))
            .Where(a => a is not null)
            .SelectMany(a => a!.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && DerivesFromAggregateRoot(t))
            .Select(t => new object[] { t });

    [Fact]
    public void AggregateDiscovery_WhenRunOnLoadedAssemblies_FindsAtLeastOneAggregate()
    {
        // Guard against the silent-skip failure mode: if Domain assemblies fail
        // to load, AllAggregateRoots returns empty and both Theory tests below
        // produce zero cases (xUnit reports "Passed" with no warnings). This
        // Fact ensures the suite fails loudly when discovery is broken.
        AllAggregateRoots().Should().NotBeEmpty(
            "Aggregate convention checks must run against real aggregates — empty discovery " +
            "means Domain assemblies aren't being loaded and convention enforcement is silently off.");
    }

    [Theory]
    [MemberData(nameof(AllAggregateRoots))]
    public void AggregateRoot_WhenInspected_HasNoPublicMutableSetters(Type aggregateType)
    {
        IEnumerable<PropertyInfo> publicMutableProperties = aggregateType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p));

        publicMutableProperties.Should().BeEmpty(
            $"{aggregateType.FullName} has public set; on properties — invariants would be " +
            "bypassable. Use private set; (or init; if the field never changes after creation) " +
            "and expose a behavior method that raises a domain event. " +
            $"Offending properties: {string.Join(", ", publicMutableProperties.Select(p => p.Name))}");
    }

    [Theory]
    [MemberData(nameof(AllAggregateRoots))]
    public void AggregateRoot_WhenInspected_ExposesNoPublicParameterlessConstructor(Type aggregateType)
    {
        ConstructorInfo[] publicCtors = aggregateType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        bool hasParameterlessPublic = publicCtors.Any(c => c.GetParameters().Length == 0);

        hasParameterlessPublic.Should().BeFalse(
            $"{aggregateType.FullName} exposes a public parameterless constructor — callers can " +
            "build the aggregate in an invalid state. Use a private/internal ctor + public static " +
            "Create factory that validates inputs and raises the creation event.");
    }

    private static bool DerivesFromAggregateRoot(Type type)
    {
        Type? current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool IsInitOnly(PropertyInfo property)
    {
        // `init;` setters are marked with the IsExternalInit modifier.
        Type[] modifiers = property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers() ?? [];
        return modifiers.Contains(typeof(IsExternalInit));
    }
}
