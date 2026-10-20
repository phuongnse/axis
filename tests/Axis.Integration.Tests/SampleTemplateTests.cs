using System.Xml.Linq;
using Axis.Configuration.Compilation;
using Axis.Configuration.Templates;
using Axis.Configuration.Tests;
using Axis.Scenarios;

namespace Axis.Integration.Tests;

/// <summary>The sample application templates, loaded as the server loads them and run through the scenario runner.</summary>
public sealed class SampleTemplateTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void The_installed_sample_template_loads_and_compiles_with_no_diagnostic()
    {
        var catalog = TemplateCatalog.Load(SampleTemplates.Path);

        Assert.Empty(catalog.Problems);
        var template = Assert.Single(catalog.Newest);
        Assert.Equal("request-approval", template.Manifest.Id);
        Assert.Equal("1.0.0", template.Manifest.Version);
        Assert.Empty(ApplicationCompiler.CompileFiles(template.Files, null, null).Diagnostics);
    }

    [Fact]
    public async Task The_scenario_of_the_request_approval_template_passes()
    {
        // The scenario command compiles a folder, and the compiler rejects template.json, so it runs a copy without it.
        var template = Assert.Single(TemplateCatalog.Load(SampleTemplates.Path).Newest);
        using var folder = new TemporaryFolder();
        foreach (var file in template.Files)
        {
            folder.With(file.Path, file.Content);
        }

        var root = RepositoryRoot();
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(
            [
                folder.Path,
                "--server", await PostgreSqlServer.AdminConnectionStringAsync(),
                "--results", Path.Combine(root, "artifacts", "test-results"),
            ],
            output,
            CancellationToken);

        Assert.True(exitCode == 0, output.ToString());
        var suite = XDocument.Load(Path.Combine(root, "artifacts", "test-results", "scenarios-RequestApproval.xml")).Root!.Element("testsuite")!;
        Assert.Equal("1", suite.Attribute("tests")!.Value);
        Assert.Equal("0", suite.Attribute("failures")!.Value);
    }

    /// <summary>The repository folder, found by walking up from the test output to <c>Axis.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Axis.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName
            ?? throw new InvalidOperationException($"Axis.slnx was not found in any folder above '{AppContext.BaseDirectory}'.");
    }
}
