using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces CLAUDE.md P0 rule per [ADR-017](../../docs/TECH_STACK.md):
/// <c>Axis.Shared.*</c> projects contain interfaces, primitives, and Result types —
/// never UnitOfWork base classes, EF helpers, or repository bases.
///
/// <para>
/// We enforce this mechanically by checking that <c>Axis.Shared.Domain</c> and
/// <c>Axis.Shared.Application</c> contain zero references to EF Core, Wolverine,
/// or persistence concerns. <c>Axis.Shared.Infrastructure</c> is allowed to
/// reference infra (it owns genuinely cross-cutting interceptors like
/// <c>TenantSchemaInterceptor</c>) but must not contain per-module concerns.
/// </para>
/// </summary>
public class SharedKernelTests
{
    /// <summary>
    /// Persistence/transport implementations that must never appear in either
    /// shared kernel project. <c>MediatR</c> is deliberately EXCLUDED here:
    /// <c>Axis.Shared.Application</c> defines the project-wide <c>ICommand</c>/
    /// <c>IQueryHandler</c> adapters and pipeline behaviors on top of MediatR
    /// (analogous to <c>HandlerLoggingMiddleware</c> in <c>Shared.Infrastructure</c>).
    /// </summary>
    private static readonly string[] PersistenceAndMessagingNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Wolverine",
        "Npgsql",
    ];

    [Fact]
    public void SharedDomain_HasZeroPersistenceOrMessagingDependencies()
    {
        Assembly sharedDomain = Conventions.TryLoad("Axis.Shared.Domain")
            ?? throw new InvalidOperationException("Axis.Shared.Domain not loadable.");

        // Shared.Domain is stricter than Shared.Application — no MediatR either.
        string[] domainBans = [.. PersistenceAndMessagingNamespaces, "MediatR"];

        TestResult result = Types.InAssembly(sharedDomain)
            .Should()
            .NotHaveDependencyOnAny(domainBans)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Axis.Shared.Domain is abstractions-only (ADR-017). " +
            $"Offending types: {FormatFailingTypes(result)}");
    }

    [Fact]
    public void SharedApplication_HasZeroPersistenceOrTransportDependencies()
    {
        Assembly sharedApp = Conventions.TryLoad("Axis.Shared.Application")
            ?? throw new InvalidOperationException("Axis.Shared.Application not loadable.");

        TestResult result = Types.InAssembly(sharedApp)
            .Should()
            .NotHaveDependencyOnAny(PersistenceAndMessagingNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Axis.Shared.Application must not depend on EF Core, Wolverine, or Npgsql (ADR-017). " +
            "MediatR adapters (ICommand/IQueryHandler/LoggingBehavior) are allowed — they are the " +
            "project-wide command/query abstraction. " +
            $"Offending types: {FormatFailingTypes(result)}");
    }

    [Fact]
    public void NoModuleAssembly_ReferencesSharedInfrastructurePersistencePrimitives()
    {
        // Future-proofing: if Axis.Shared.Infrastructure ever ships a UnitOfWork
        // base class or DbContext base class, modules must not depend on it
        // (per ADR-017 we inlined UnitOfWork into each module's Infrastructure).
        // This test fails if a *.Persistence type from Shared.Infrastructure
        // becomes a dependency of any module — flagging the regression.
        string[] forbiddenSharedInfraNamespaces =
        [
            "Axis.Shared.Infrastructure.Persistence.UnitOfWorkBase",
            "Axis.Shared.Infrastructure.Persistence.AxisDbContext",
        ];

        foreach (string module in Conventions.ModuleNames)
        {
            foreach (string layer in Conventions.LayerNames)
            {
                Assembly? assembly = Conventions.TryLoadModuleLayer(module, layer);
                if (assembly is null)
                    continue;

                TestResult result = Types.InAssembly(assembly)
                    .Should()
                    .NotHaveDependencyOnAny(forbiddenSharedInfraNamespaces)
                    .GetResult();

                result.IsSuccessful.Should().BeTrue(
                    $"Axis.{module}.{layer} depends on a removed Axis.Shared.Infrastructure persistence " +
                    "base class (ADR-017 inlined UnitOfWork + DbContext per module). " +
                    $"Offending types: {FormatFailingTypes(result)}");
            }
        }
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
