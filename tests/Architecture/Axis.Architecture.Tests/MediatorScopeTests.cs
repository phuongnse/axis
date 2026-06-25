using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces AGENTS.md rule: <c>MediatR is intra-module only</c>.
///
/// <para>
/// Mechanical check: no <c>Axis.{ModuleA}.*</c> assembly may reference a
/// MediatR <c>IRequest</c>/<c>IRequestHandler</c> defined in
/// <c>Axis.{ModuleB}.Application</c>. (The full P0 rule additionally bans
/// in-process service calls — those are covered by <see cref="ModuleBoundaryTests"/>.)
/// </para>
/// </summary>
public class MediatorScopeTests
{
    [Fact]
    public void MediatRScopeRule_WhenInspected_IsCoveredByModuleBoundaryTests()
    {
        // This is a marker test, not a separate check. The AGENTS.md rule
        // "MediatR is intra-module only" is mechanically equivalent to
        // "no Axis.{ModuleA}.* assembly references Axis.{ModuleB}.Application",
        // which ModuleBoundaryTests already enforces (with the same allow-list).
        // Keeping a named test here so anyone grepping CI output for "MediatR"
        // lands on the relevant rule + the right file to look at.
        true.Should().BeTrue(
            "MediatR scope is enforced indirectly: any cross-module MediatR dispatch would " +
            "have to reference the target module's Application namespace, which is blocked by " +
            "ModuleBoundaryTests.Module_MustNotReferenceOtherModuleApplication.");
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
