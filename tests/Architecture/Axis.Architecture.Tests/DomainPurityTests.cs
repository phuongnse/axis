using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Axis.Architecture.Tests;

/// <summary>
/// Enforces AGENTS.md P0 rule: <c>Domain: zero external dependencies</c>.
/// Domain projects must contain only entities, value objects, domain events,
/// and pure C# — no EF Core, MediatR, ASP.NET, MailKit, OpenIddict,
/// Redis, Serilog, or any infrastructure namespace.
/// </summary>
public class DomainPurityTests
{
    /// <summary>
    /// Infrastructure namespaces that must NEVER appear in any Domain assembly.
    /// Add new bans here when a new infra concern arrives; never narrow this list
    /// without updating AGENTS.md.
    /// </summary>
    private static readonly string[] BannedNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "MediatR",
        "MailKit",
        "Npgsql",
        "OpenIddict",
        "StackExchange.Redis",
        "Serilog",
        "FluentValidation",
    ];

    public static IEnumerable<object[]> DomainAssemblies() =>
        Conventions.ModuleNames
            .Select(m => (module: m, asm: Conventions.TryLoadModuleLayer(m, "Domain")))
            .Where(t => t.asm is not null)
            .Select(t => new object[] { t.module, t.asm! });

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void DomainAssembly_WhenInspected_HasZeroInfrastructureDependencies(string module, Assembly domainAssembly)
    {
        TestResult result = Types.InAssembly(domainAssembly)
            .Should()
            .NotHaveDependencyOnAny(BannedNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Axis.{module}.Domain must not depend on infrastructure namespaces (AGENTS.md P0). " +
            $"Offending types: {FormatFailingTypes(result)}");
    }

    [Fact]
    public void SharedDomainAssembly_WhenInspected_HasZeroInfrastructureDependencies()
    {
        Assembly sharedDomain = Conventions.TryLoad("Axis.Shared.Domain")
            ?? throw new InvalidOperationException("Axis.Shared.Domain not loadable.");

        TestResult result = Types.InAssembly(sharedDomain)
            .Should()
            .NotHaveDependencyOnAny(BannedNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Axis.Shared.Domain must not depend on infrastructure namespaces (ADR-017 + AGENTS.md P0). " +
            $"Offending types: {FormatFailingTypes(result)}");
    }

    private static string FormatFailingTypes(TestResult result) =>
        result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(NetArchTest did not list specific types)"
            : string.Join(", ", result.FailingTypeNames);
}
