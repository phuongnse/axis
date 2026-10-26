using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ApplicationFileStorageCompilerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e02";

    [Fact]
    public void A_manifest_with_a_file_storage_binding_compiles_into_the_model_and_survives_the_stored_resources()
    {
        using var folder = Folder("""{ "binding": "Files" }""");

        var result = ApplicationCompiler.Compile(folder.Path);
        var rebuilt = ApplicationCompiler.Compile(result.Resources);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal("Files", result.Model!.Manifest.FileStorage!.Binding);
        Assert.Equal("Files", rebuilt.Model!.Manifest.FileStorage!.Binding);
    }

    [Fact]
    public void A_manifest_without_file_storage_names_no_binding()
    {
        using var folder = Folder(null);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Null(result.Model!.Manifest.FileStorage);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "binding": "not a name" }""")]
    [InlineData("""{ "binding": "Files", "kind": "storage" }""")]
    public void A_file_storage_that_is_not_one_binding_name_is_a_schema_error(string fileStorage)
    {
        using var folder = Folder(fileStorage);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation);
        Assert.Null(result.Model);
    }

    private static TemporaryFolder Folder(string? fileStorage) =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1{{(fileStorage is null ? "" : $", \"fileStorage\": {fileStorage}")}} }
            """);
}
