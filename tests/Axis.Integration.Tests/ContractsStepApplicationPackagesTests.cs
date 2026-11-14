using System.Text.Json.Nodes;
using Axis.Admin;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Data.Records;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using static Axis.Integration.Tests.ContractTestModules;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>contracts</c> step compiles the snapshot with the same <c>Extensions:ApplicationPackages</c> as the compile that stores
/// the release, see docs/reference/extension-packages.md#an-applications-packages. An application that holds a package must not
/// make the step skip the contract scenarios of its modules without a word.
/// </summary>
public sealed class ContractsStepApplicationPackagesTests
{
    private const string Manifest = """
        { "id": "tools", "version": "1.0.0", "label": "Tools", "language": "python", "adapter": "adapter.py", "lock": "requirements.txt",
          "operations": [ { "name": "sum", "type": "adapter.sum", "idempotent": true, "input": [], "output": [] } ] }
        """;

    private static List<ResourceContent> Files(string app)
    {
        var files = Directory.EnumerateFiles(app, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new ResourceContent(Path.GetRelativePath(app, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
        files.Add(new ResourceContent("packages/tools/axis-package.json", Manifest));
        files.Add(new ResourceContent("packages/tools/adapter.py", "import sys\n"));
        files.Add(new ResourceContent("packages/tools/requirements.txt", "requests==2.32.0\n"));
        return files;
    }

    private static async Task<(bool Passed, List<Diagnostic> Diagnostics)> RunAsync(bool applicationPackages)
    {
        using var modules = ModulesWithScenario("submitted");
        var activation = new TenantReleaseActivation(
            100,
            TimeSpan.FromMinutes(1),
            new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(new ConfigurationBuilder().Build()))),
            TextWriter.Null,
            TextWriter.Null,
            CancellationToken.None);
        var diagnostics = new List<Diagnostic>();

        var passed = await activation.ContractsAsync(
            "tenant",
            scenarioServer: null,
            Files(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp")),
            PackageCatalog.Empty,
            ModuleCatalog.Load(modules.Path),
            diagnostics,
            new JsonArray(),
            TimeSpan.FromSeconds(30),
            applicationPackages);

        return (passed, diagnostics);
    }

    [Fact]
    public async Task With_application_packages_on_the_contract_scenarios_of_the_modules_still_run()
    {
        var (passed, diagnostics) = await RunAsync(applicationPackages: true);

        Assert.False(passed);
        Assert.Equal(DiagnosticCodes.ScenarioServerMissing, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task With_application_packages_off_the_step_runs_nothing_and_the_next_compile_reports_the_package()
    {
        var (passed, diagnostics) = await RunAsync(applicationPackages: false);

        Assert.True(passed);
        Assert.Empty(diagnostics);
    }
}
