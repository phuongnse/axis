using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Realms;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;

namespace Axis.Configuration.Tests;

public sealed class ApplicationPackageCompilerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string ManifestPath = "packages/tools/axis-package.json";
    private const string Adapter = "import sys\n\nprint('sum')\r\n";
    private const string Lock = "requests==2.32.0\n";

    private const string ToolsManifest = """
        { "id": "tools", "version": "2.3.4", "label": "Tools", "language": "python",
          "adapter": "adapter.py", "lock": "requirements.txt",
          "operations": [
            { "name": "sum", "type": "adapter.sum", "idempotent": true,
              "input": [ { "name": "a", "type": "integer", "required": true }, { "name": "b", "type": "integer", "required": true } ],
              "output": [ { "name": "total", "type": "integer" } ] }
          ] }
        """;

    private const string SumStep = """
        { "name": "add", "type": "operation", "operation": "tools/sum",
          "input": { "a": "1", "b": "amount" }, "output": { "total": "amount" }, "next": "done" }
        """;

    [Fact]
    public void A_step_calls_an_operation_of_the_applications_own_package_without_listing_it()
    {
        using var folder = Folder(SumStep);

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(("add", "tools", "2.3.4", "sum", "done"), (step.Name, step.Package, step.Version, step.Operation, step.Next));
        Assert.Equal([("a", "1"), ("b", "amount")], step.Input.Select(input => (input.Input, input.Value!.Expression)));
        Assert.Equal([new ConnectorOutputModel("total", "amount")], step.Output);
        Assert.Empty(result.Model.Packages);
    }

    [Fact]
    public void A_wrong_input_field_of_the_applications_own_package_is_reported_at_its_key()
    {
        using var folder = Folder("""
            { "name": "add", "type": "operation", "operation": "tools/sum",
              "input": { "a": "1", "b": "2", "c": "3" }, "next": "done" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("processes/order-review.json", "/steps/0/input/c"), (diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_step_naming_a_package_that_is_neither_listed_nor_in_the_folder_is_an_unknown_package()
    {
        using var folder = Folder("""
            { "name": "add", "type": "operation", "operation": "other/sum", "input": {}, "next": "done" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownStepPackage, "processes/order-review.json", "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void An_unknown_operation_of_the_applications_own_package_is_an_unknown_operation()
    {
        using var folder = Folder("""
            { "name": "add", "type": "operation", "operation": "tools/multiply", "input": {}, "next": "done" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownPackageOperation, "processes/order-review.json", "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal("The operation 'multiply' was not found. Package 'tools' 2.3.4 has no operation with that name.", diagnostic.Message);
    }

    [Fact]
    public void A_package_id_that_an_installed_package_uses_is_reported_at_the_manifest()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(null)
            .With("packages/test-package/axis-package.json", ToolsManifest.Replace("\"tools\"", "\"test-package\"", StringComparison.Ordinal))
            .With("packages/test-package/adapter.py", Adapter)
            .With("packages/test-package/requirements.txt", Lock);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions, null, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.ApplicationPackageIdTaken);
        Assert.Equal(("packages/test-package/axis-package.json", "/id"), (diagnostic.File, diagnostic.Path));
        Assert.Equal(
            "Package 'test-package': The package id 'test-package' is already used by a package installed for the installation. Choose another id.",
            diagnostic.Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_manifest_that_breaks_the_schema_is_reported_at_the_manifest()
    {
        using var folder = Folder(null)
            .With(ManifestPath, ToolsManifest.Replace("\"label\": \"Tools\", ", "", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, ManifestPath, ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.StartsWith("Package 'tools': ", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_broken_package_is_reported_once_and_its_steps_are_not_reported_as_unknown()
    {
        using var folder = Folder(SumStep)
            .With(ManifestPath, ToolsManifest.Replace("\"label\": \"Tools\", ", "", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        Assert.Equal([DiagnosticCodes.PackageManifestInvalid], result.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_manifest_rule_the_schema_cannot_check_is_reported_at_the_manifest()
    {
        using var folder = Folder(null)
            .With(ManifestPath, ToolsManifest.Replace("\"idempotent\": true,", "\"idempotent\": true, \"timeout\": \"PT10M\",", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidPackageTimeout, ManifestPath, "/operations/0/timeout"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_manifest_id_that_differs_from_its_folder_is_reported_at_the_id()
    {
        using var folder = Folder(null)
            .With(ManifestPath, ToolsManifest.Replace("\"tools\"", "\"other\"", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageFolderMismatch, ManifestPath, "/id"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_folder_without_a_manifest_and_a_file_outside_a_package_folder_are_reported()
    {
        using var folder = Folder(null)
            .Without(ManifestPath)
            .With("packages/readme.md", "Notes");

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        Assert.Equal(
            [(DiagnosticCodes.PackageManifestInvalid, "packages/readme.md"), (DiagnosticCodes.PackageManifestInvalid, ManifestPath)],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File)).OrderBy(pair => pair.File, StringComparer.Ordinal));
    }

    [Fact]
    public void Any_application_package_is_reported_at_its_manifest_while_the_setting_is_off()
    {
        using var folder = Folder(SumStep);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ApplicationPackagesOff, ManifestPath, ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            "Application packages are off in this installation (Extensions:ApplicationPackages). Remove the package or ask the operator to turn them on.",
            diagnostic.Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_manifest_only_customer_hosted_package_compiles_while_the_setting_is_off_and_marks_its_steps()
    {
        using var folder = CustomerHostedFolder();

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(result.Model!.Processes).Steps[0]);
        Assert.Equal(("tools", "2.3.4", true), (step.Package, step.Version, step.CustomerHosted));
        Assert.Equal([ManifestPath], result.Resources.Where(resource => resource.Path.StartsWith("packages/", StringComparison.Ordinal)).Select(resource => resource.Path));
    }

    [Fact]
    public void A_file_next_to_the_manifest_of_a_customer_hosted_package_is_reported_at_that_file()
    {
        using var folder = CustomerHostedFolder().With("packages/tools/adapter.py", Adapter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, "packages/tools/adapter.py", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal("Package 'tools': A customer-hosted package holds only its manifest. Remove 'adapter.py'.", diagnostic.Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_package_that_axis_runs_is_still_refused_while_the_setting_is_off_beside_a_customer_hosted_one()
    {
        using var folder = CustomerHostedFolder()
            .With("packages/other/axis-package.json", ToolsManifest.Replace("\"tools\"", "\"other\"", StringComparison.Ordinal))
            .With("packages/other/adapter.py", Adapter)
            .With("packages/other/requirements.txt", Lock);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ApplicationPackagesOff, "packages/other/axis-package.json"), (diagnostic.Code, diagnostic.File));
    }

    [Fact]
    public void A_release_that_holds_a_customer_hosted_package_rebuilds_with_the_flag()
    {
        using var folder = CustomerHostedFolder();
        var compiled = ApplicationCompiler.Compile(folder.Path);
        Assert.Empty(compiled.Diagnostics);
        var id = Guid.NewGuid();
        var release = new Release
        {
            Id = id,
            ApplicationId = Guid.Parse(ApplicationId),
            ContentHash = compiled.ContentHash!,
            CreatedAt = DateTimeOffset.UtcNow,
            Resources = [.. compiled.Resources.Select(resource => new ReleaseResource { ReleaseId = id, Path = resource.Path, Content = resource.Content })],
        };

        var model = ReleaseCompiler.BuildModel(release);

        ModelAssert.Equal(compiled.Model!, model);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(model.Processes).Steps[0]);
        Assert.True(step.CustomerHosted);
    }

    [Fact]
    public void A_file_of_a_package_is_no_resource_and_the_release_holds_every_package_file_as_written()
    {
        const string Fixture = """
            { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Fixture", "formatVersion": 1,
              "fields": [ { "name": "note", "type": "text" } ] }
            """;
        using var folder = Folder(SumStep)
            .With("packages/tools/tests/fixture.json", Fixture);

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(result.Model!.Entities, entity => entity.Name == "Fixture");
        var packageFiles = result.Resources.Where(resource => resource.Path.StartsWith("packages/", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                ("packages/tools/adapter.py", Adapter),
                (ManifestPath, ToolsManifest),
                ("packages/tools/requirements.txt", Lock),
                ("packages/tools/tests/fixture.json", Fixture),
            ],
            packageFiles.Select(resource => (resource.Path, resource.Content)));
        Assert.Equal(result.Resources.Select(resource => resource.Path).Order(StringComparer.Ordinal), result.Resources.Select(resource => resource.Path));
    }

    [Fact]
    public void A_file_of_a_package_that_is_not_utf8_text_is_reported_at_the_file()
    {
        using var folder = Folder(SumStep);
        File.WriteAllBytes(Path.Combine(folder.Path, "packages", "tools", "vendored.bin"), [0xFF, 0xFE, 0x00, 0xC3]);

        var result = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnreadableFile, "packages/tools/vendored.bin", "The file is not UTF-8 text."),
            (diagnostic.Code, diagnostic.File, diagnostic.Message));
    }

    [Fact]
    public void A_folder_and_the_same_files_in_memory_give_the_same_content_hash()
    {
        using var folder = Folder(SumStep);

        var fromFolder = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);
        var fromFiles = ApplicationCompiler.CompileFiles(
            [.. fromFolder.Resources.Select(resource => new ResourceContent(resource.Path, resource.Content))],
            null,
            null,
            applicationPackages: true);

        Assert.Empty(fromFiles.Diagnostics);
        Assert.Equal(fromFolder.ContentHash, fromFiles.ContentHash);
    }

    [Fact]
    public void A_release_that_holds_an_application_package_rebuilds_to_the_same_hash_while_the_setting_is_off()
    {
        using var folder = Folder(SumStep);
        var compiled = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);
        Assert.Empty(compiled.Diagnostics);
        var id = Guid.NewGuid();
        var release = new Release
        {
            Id = id,
            ApplicationId = Guid.Parse(ApplicationId),
            ContentHash = compiled.ContentHash!,
            CreatedAt = DateTimeOffset.UtcNow,
            Resources = [.. compiled.Resources.Select(resource => new ReleaseResource { ReleaseId = id, Path = resource.Path, Content = resource.Content })],
        };

        var model = ReleaseCompiler.BuildModel(release);

        ModelAssert.Equal(compiled.Model!, model);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(model.Processes).Steps[0]);
        Assert.Equal(("tools", "2.3.4"), (step.Package, step.Version));
    }

    [Fact]
    public void A_release_that_holds_an_application_package_reads_as_a_realm_application_without_reading_the_package_files()
    {
        using var folder = Folder(SumStep).With("packages/tools/tests/site.json", """{ "kind": "site", "path": "/fixture" }""");
        var compiled = ApplicationCompiler.Compile(folder.Path, applicationPackages: true);
        Assert.Empty(compiled.Diagnostics);
        var id = Guid.NewGuid();
        var release = new Release
        {
            Id = id,
            ApplicationId = Guid.Parse(ApplicationId),
            ContentHash = compiled.ContentHash!,
            CreatedAt = DateTimeOffset.UtcNow,
            Resources = [.. compiled.Resources.Select(resource => new ReleaseResource { ReleaseId = id, Path = resource.Path, Content = resource.Content })],
        };
        Assert.Contains(release.Resources, resource => resource.Path == "packages/tools/adapter.py");

        var application = RealmApplication.FromRelease("Sample", release);

        Assert.Empty(application.Sites);
    }

    /// <summary>The application of <see cref="Folder"/> whose package <c>tools</c> is customer-hosted, so it holds only its manifest.</summary>
    private static TemporaryFolder CustomerHostedFolder() =>
        Folder(SumStep)
            .With(ManifestPath, ToolsManifest.Replace("\"label\": \"Tools\",", "\"label\": \"Tools\", \"runner\": \"customer\",", StringComparison.Ordinal))
            .Without("packages/tools/adapter.py")
            .Without("packages/tools/requirements.txt");

    /// <summary>An application with an <c>Order</c> entity, the package <c>tools</c> and, when given, a process whose first step is <paramref name="step"/>.</summary>
    private static TemporaryFolder Folder(string? step)
    {
        var folder = new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1 }
                """)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [ { "name": "amount", "type": "integer" } ] }
                """)
            .With(ManifestPath, ToolsManifest)
            .With("packages/tools/adapter.py", Adapter)
            .With("packages/tools/requirements.txt", Lock);
        return step is null
            ? folder
            : folder.With("processes/order-review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "add",
                  "steps": [ {{step}}, { "name": "done", "type": "end" } ] }
                """);
    }
}
