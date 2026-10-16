using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Tests;

public sealed class ContentHashTests
{
    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    private const string Order = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "maxLength": 20 } ] }
        """;

    private const string Customer = """
        { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1,
          "fields": [ { "name": "name", "type": "text" } ] }
        """;

    [Fact]
    public void Hash_is_sha256_over_path_and_content_lines_in_path_order()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ContentHash.Compute([]));
        Assert.Equal(
            "448373c479c7611326f9d5831449b9a1cb309ff1f06f9f32d1a46ca6f3f11486",
            ContentHash.Compute([new ResourceContent("b/c.json", "[]"), new ResourceContent("a.json", """{"x":1}""")]));
    }

    [Fact]
    public void Pinned_packages_are_hashed_after_the_resources_in_id_order()
    {
        ResourceContent[] resources = [new("a.json", """{"x":1}""")];
        PinnedPackageModel[] pins = [new("b", "^2.0.0", "2.0.0"), new("a", "1.0.0", "1.0.0")];

        var hash = ContentHash.Compute(resources, pins);

        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("a.json\n{\"x\":1}\n/packages/a\n1.0.0\n/packages/b\n2.0.0\n"))),
            hash);
        Assert.NotEqual(hash, ContentHash.Compute(resources, [pins[0] with { Version = "2.1.0" }, pins[1]]));
        Assert.Equal(ContentHash.Compute(resources), ContentHash.Compute(resources, []));
    }

    [Fact]
    public void An_application_without_packages_keeps_the_hash_of_its_resources()
    {
        using var folder = SampleFolder();

        var result = ApplicationCompiler.Compile(folder.Path, [new PackageVersion("test-package", "1.0.0")]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(ContentHash.Compute(result.Resources), result.ContentHash);
    }

    [Fact]
    public void A_pin_to_another_installed_version_changes_the_hash()
    {
        using var folder = new TemporaryFolder().With("application.json", """
            { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
              "packages": [{ "id": "test-package", "version": "^1.0.0" }] }
            """);

        var first = ApplicationCompiler.Compile(folder.Path, [new PackageVersion("test-package", "1.0.0"), new PackageVersion("test-package", "1.2.0")]);
        var second = ApplicationCompiler.Compile(folder.Path, [new PackageVersion("test-package", "1.3.0"), new PackageVersion("test-package", "1.2.0")]);

        Assert.Empty(first.Diagnostics);
        Assert.Empty(second.Diagnostics);
        Assert.Equal(first.Resources.Select(resource => resource.Content), second.Resources.Select(resource => resource.Content));
        Assert.Equal(ContentHash.Compute(first.Resources, first.Model!.Packages), first.ContentHash);
        Assert.NotEqual(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void Same_folder_compiled_twice_has_the_same_hash()
    {
        using var folder = SampleFolder();

        var first = ApplicationCompiler.Compile(folder.Path);
        var second = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(first.Diagnostics);
        Assert.NotNull(first.ContentHash);
        Assert.Matches("^[0-9a-f]{64}$", first.ContentHash);
        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void Resources_hold_the_canonical_content_of_every_file_in_path_order()
    {
        using var folder = SampleFolder();

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(["application.json", "entities/customer.json", "entities/order.json"], result.Resources.Select(resource => resource.Path));
        Assert.Equal(
            """{"fields":[{"maxLength":20,"name":"number","type":"text"}],"formatVersion":1,"id":"11111111-1111-4111-8111-111111111111","kind":"entity","name":"Order"}""",
            result.Resources[2].Content);
        Assert.Equal(ContentHash.Compute(result.Resources), result.ContentHash);
    }

    [Fact]
    public void Formatting_only_changes_keep_the_hash()
    {
        using var original = SampleFolder();
        using var reformatted = new TemporaryFolder()
            .With("application.json", "{\r\n    \"formatVersion\": 1,\r\n    \"name\": \"Sample\",\r\n    \"kind\": \"application\",\r\n    \"id\": \"0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01\"\r\n}\r\n")
            .With("entities/order.json", """
                {
                        "fields": [{"type":"text","maxLength":20.0,"name":"number"}],
                  "name":"Order","kind":"entity",
                  "formatVersion":1, "id":"11111111-1111-4111-8111-111111111111"
                }
                """)
            .With("entities/customer.json", Customer.ReplaceLineEndings("\r\n"));

        var expected = ApplicationCompiler.Compile(original.Path);
        var actual = ApplicationCompiler.Compile(reformatted.Path);

        Assert.Empty(actual.Diagnostics);
        Assert.NotNull(expected.ContentHash);
        Assert.Equal(expected.ContentHash, actual.ContentHash);
    }

    [Fact]
    public void Changed_value_changes_the_hash()
    {
        using var original = SampleFolder();
        using var changed = SampleFolder(order: Order.Replace("\"maxLength\": 20", "\"maxLength\": 21", StringComparison.Ordinal));

        AssertDifferentHashes(original, changed);
    }

    [Fact]
    public void Added_file_changes_the_hash()
    {
        using var original = SampleFolder();
        using var added = SampleFolder().With("entities/invoice.json", """
            { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "Invoice", "formatVersion": 1,
              "fields": [ { "name": "number", "type": "text" } ] }
            """);

        AssertDifferentHashes(original, added);
    }

    [Fact]
    public void Added_migration_file_changes_the_hash()
    {
        using var original = SampleFolder();
        using var added = SampleFolder().With("migrations/0001-fill-number.json", Migration("'n/a'"));

        AssertDifferentHashes(original, added);
    }

    [Fact]
    public void Changed_value_in_a_migration_file_changes_the_hash()
    {
        using var original = SampleFolder().With("migrations/0001-fill-number.json", Migration("'n/a'"));
        using var changed = SampleFolder().With("migrations/0001-fill-number.json", Migration("'none'"));

        AssertDifferentHashes(original, changed);
    }

    [Fact]
    public void Removed_file_changes_the_hash()
    {
        using var original = SampleFolder();
        using var removed = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", Order);

        AssertDifferentHashes(original, removed);
    }

    [Fact]
    public void Renamed_file_changes_the_hash()
    {
        using var original = SampleFolder();
        using var renamed = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", Order)
            .With("entities/client.json", Customer);

        AssertDifferentHashes(original, renamed);
    }

    [Fact]
    public void Folder_with_an_error_has_no_hash_and_no_resources()
    {
        using var folder = SampleFolder().With("entities/broken.json", "{ \"kind\": ");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.True(result.HasErrors);
        Assert.Null(result.ContentHash);
        Assert.Empty(result.Resources);
    }

    private static string Migration(string value) => $$"""
        { "id": "44444444-4444-4444-8444-444444444444", "kind": "migration", "name": "FillNumber", "formatVersion": 1, "entity": "Order",
          "changes": [ { "type": "fill", "field": "number", "value": "{{value}}" } ] }
        """;

    private static TemporaryFolder SampleFolder(string order = Order) =>
        new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", order)
            .With("entities/customer.json", Customer);

    private static void AssertDifferentHashes(TemporaryFolder original, TemporaryFolder changed)
    {
        var expected = ApplicationCompiler.Compile(original.Path);
        var actual = ApplicationCompiler.Compile(changed.Path);

        Assert.Empty(actual.Diagnostics);
        Assert.NotNull(expected.ContentHash);
        Assert.NotNull(actual.ContentHash);
        Assert.NotEqual(expected.ContentHash, actual.ContentHash);
    }
}
