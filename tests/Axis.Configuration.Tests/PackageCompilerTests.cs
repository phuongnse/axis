using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

public sealed class PackageCompilerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";

    private static readonly PackageVersion[] _installed = [new("test-package", "1.0.0"), new("test-package", "1.2.0")];

    [Fact]
    public void A_caret_range_pins_the_highest_installed_version_with_the_same_major_version()
    {
        using var folder = Folder("""[{ "id": "test-package", "version": "^1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        Assert.Empty(result.Diagnostics);
        Assert.Equal([new PinnedPackageModel("test-package", "^1.0.0", "1.2.0")], result.Model!.Packages);
        Assert.Equal("^1.0.0", Assert.Single(result.Model.Manifest.Packages).Version);
    }

    [Theory]
    [InlineData("~1.0.0", "1.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("^1.1.0", "1.2.0")]
    public void Each_range_form_pins_the_highest_installed_version_in_it(string range, string pinned)
    {
        using var folder = Folder($$"""[{ "id": "test-package", "version": "{{range}}" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(pinned, Assert.Single(result.Model!.Packages).Version);
    }

    [Fact]
    public void A_range_that_no_installed_version_meets_is_reported_at_its_version()
    {
        using var folder = Folder("""[{ "id": "test-package", "version": "^2.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.NoPackageVersionInRange, "application.json", "/packages/0/version", Guid.Parse(ApplicationId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'1.0.0', '1.2.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_package_with_no_installed_version_is_unknown_at_its_id()
    {
        using var folder = Folder("""[{ "id": "other-package", "version": "^1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownPackage, "application.json", "/packages/0/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'other-package'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_installed_packages_every_package_is_unknown()
    {
        using var folder = Folder("""[{ "id": "test-package", "version": "^1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownPackage, "/packages/0/id"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("1.x")]
    [InlineData("1.0")]
    [InlineData(">=1.0.0")]
    [InlineData("^01.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData(" 1.0.0")]
    public void A_range_in_another_form_is_invalid_at_its_version(string range)
    {
        using var folder = Folder($$"""[{ "id": "test-package", "version": "{{range}}" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidPackageRange, "application.json", "/packages/0/version"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'^1.2.0'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_range_of_an_unknown_package_reports_both()
    {
        using var folder = Folder("""[{ "id": "other-package", "version": "1.x" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        Assert.Equal(
            [(DiagnosticCodes.UnknownPackage, "/packages/0/id"), (DiagnosticCodes.InvalidPackageRange, "/packages/0/version")],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void A_package_id_listed_twice_is_reported_at_the_later_id_only()
    {
        using var folder = Folder("""
            [{ "id": "test-package", "version": "^1.0.0" }, { "id": "test-package", "version": "nonsense" }]
            """);

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateApplicationPackage, "application.json", "/packages/1/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData("""[{ "id": "Test-Package", "version": "^1.0.0" }]""", "/packages/0/id")]
    [InlineData("""[{ "id": "test-package" }]""", "/packages/0")]
    [InlineData("""[{ "id": "test-package", "version": "" }]""", "/packages/0/version")]
    [InlineData("""[{ "id": "test-package", "version": "^1.0.0", "url": "x" }]""", "/packages/0/url")]
    public void A_malformed_entry_is_a_schema_violation(string packages, string path)
    {
        using var folder = Folder(packages);

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.Path == path);
    }

    [Fact]
    public void An_application_without_packages_pins_nothing()
    {
        using var folder = new TemporaryFolder().With("application.json", PresentationCompilerTests.Manifest);

        var result = ApplicationCompiler.Compile(folder.Path, _installed);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Model!.Packages);
    }

    private static TemporaryFolder Folder(string packages) =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1, "packages": {{packages}} }
            """);
}
