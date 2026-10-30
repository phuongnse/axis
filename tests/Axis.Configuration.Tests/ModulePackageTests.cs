using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

public sealed class ModulePackageTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ProcessFile = "modules/approvals/processes/Review.json";
    private const string ManifestFile = "modules/approvals/module.json";
    private const string SumStep = """{ "name": "add", "type": "operation", "operation": "test-package/sum", "input": { "a": "1", "b": "2" }, "next": "done" }""";

    [Fact]
    public void A_module_process_calls_a_package_of_its_module_and_pins_the_highest_version_in_range()
    {
        using var packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var modules = Modules(modulePackages: ModulePackages("^1.0.0"), step: SumStep);
        using var folder = Application();

        var compiled = Compile(folder, modules, packages);

        Assert.Empty(compiled.Diagnostics);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(compiled.Model!.Processes).Steps[0]);
        Assert.Equal(("test-package", "1.1.0", "sum"), (step.Package, step.Version, step.Operation));
        Assert.Equal([new PinnedPackageModel("test-package", "^1.0.0", "1.1.0")], compiled.Model.Packages);
    }

    [Fact]
    public void A_module_reuses_the_pin_of_the_application_when_it_is_in_the_range()
    {
        using var packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var modules = Modules(modulePackages: ModulePackages("^1.0.0"), step: SumStep);
        using var folder = Application(applicationPackages: ApplicationPackages("1.0.0"));

        var compiled = Compile(folder, modules, packages);

        Assert.Empty(compiled.Diagnostics);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(compiled.Model!.Processes).Steps[0]);
        Assert.Equal("1.0.0", step.Version);
        Assert.Equal([new PinnedPackageModel("test-package", "1.0.0", "1.0.0")], compiled.Model.Packages);
    }

    [Fact]
    public void A_module_entry_is_reported_when_the_pin_of_the_application_is_outside_its_range()
    {
        using var packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var modules = Modules(modulePackages: ModulePackages("^1.1.0"), step: SumStep);
        using var folder = Application(applicationPackages: ApplicationPackages("1.0.0"));

        var compiled = Compile(folder, modules, packages);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.NoPackageVersionInRange, ManifestFile, "/packages/0/version"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("application pins package 'test-package' at '1.0.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_process_calling_a_package_its_module_does_not_list_is_reported_in_the_process_file()
    {
        using var packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var modules = Modules(modulePackages: ModulePackages("^1.0.0"), step: SumStep.Replace("test-package/sum", "other-package/sum", StringComparison.Ordinal));
        using var folder = Application();

        var compiled = Compile(folder, modules, packages);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownStepPackage, ProcessFile, "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_package_that_is_not_installed_is_reported_in_the_module_manifest()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var modules = Modules(modulePackages: """, "packages": [{ "id": "missing-package", "version": "^1.0.0" }]""");
        using var folder = Application();

        var compiled = Compile(folder, modules, packages);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownPackage, ManifestFile, "/packages/0/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void An_application_process_cannot_call_a_package_only_a_module_lists()
    {
        using var packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var modules = Modules(modulePackages: ModulePackages("^1.0.0"), step: SumStep);
        using var folder = Application().With("processes/Audit.json", """
            { "id": "44444444-4444-4444-8444-444444444431", "kind": "process", "name": "Audit", "formatVersion": 1,
              "entity": "Invoice", "start": "add", "steps": [
                { "name": "add", "type": "operation", "operation": "test-package/sum", "input": { "a": "1", "b": "2" }, "next": "done" },
                { "name": "done", "type": "end" }
              ] }
            """);

        var compiled = Compile(folder, modules, packages);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownStepPackage, "processes/Audit.json", "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void Modules_that_list_a_package_without_a_common_version_are_all_reported()
    {
        using var packages = new TemporaryFolder().Install("1.0.0", "2.0.0");
        using var modules = Modules(modulePackages: ModulePackages("^1.0.0"), step: SumStep);
        modules.With("other/1.0.0/module.json", $$"""
            { "kind": "module", "id": "other", "version": "1.0.0", "label": "Other"{{ModulePackages("^2.0.0")}} }
            """);
        using var folder = Application(applicationModules: """{ "id": "approvals", "version": "1.0.0" }, { "id": "other", "version": "1.0.0" }""");

        var compiled = Compile(folder, modules, packages);

        Assert.Equal(
            [
                (DiagnosticCodes.NoPackageVersionInRange, ManifestFile, "/packages/0/version"),
                (DiagnosticCodes.NoPackageVersionInRange, "modules/other/module.json", "/packages/0/version"),
            ],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)).Order().ToList());
        Assert.Null(compiled.Model);
    }

    internal static CompilationResult Compile(TemporaryFolder application, TemporaryFolder modules, TemporaryFolder packages) =>
        ApplicationCompiler.Compile(application.Path, PackageCatalog.Load(packages.Path).Versions, ModuleCatalog.Load(modules.Path).Modules);

    internal static string ModulePackages(string range) => $$""", "packages": [{ "id": "test-package", "version": "{{range}}" }]""";

    internal static string ApplicationPackages(string range) => $$""", "packages": [{ "id": "test-package", "version": "{{range}}" }]""";

    /// <summary>The module <c>approvals</c>, whose process <c>Review</c> has the given step before its end.</summary>
    internal static TemporaryFolder Modules(string modulePackages = "", string step = "")
    {
        var start = step.Length == 0 ? "done" : "add";
        return new TemporaryFolder()
            .With("approvals/1.0.0/module.json", $$"""
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals"{{modulePackages}} }
                """)
            .With("approvals/1.0.0/Approval.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21", "kind": "entity", "name": "Approval", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" } ] }
                """)
            .With("approvals/1.0.0/processes/Review.json", $$"""
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41", "kind": "process", "name": "Review", "formatVersion": 1,
                  "entity": "Approval", "start": "{{start}}", "steps": [
                    {{(step.Length == 0 ? "" : step + ",")}}
                    { "name": "done", "type": "end" }
                  ] }
                """);
    }

    internal static TemporaryFolder Application(string applicationPackages = "", string applicationModules = """{ "id": "approvals", "version": "1.0.0" }""") =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{{applicationModules}}]{{applicationPackages}} }
                """)
            .With("entities/Invoice.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "entity", "name": "Invoice", "formatVersion": 1,
                  "fields": [ { "name": "number", "type": "text" } ] }
                """);
}
