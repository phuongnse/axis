using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class SeedCompilerTests
{
    private const string FirstRecordId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1";
    private const string SecondRecordId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2";

    [Fact]
    public void Valid_seed_is_in_the_model_with_its_entity_and_records_and_changes_the_content_hash()
    {
        using var withoutSeed = Folder();
        using var withSeed = Folder().With("seeds/orders.json", Seed(
            "Orders",
            "77777777-7777-4777-8777-777777777771",
            "Order",
            Record(FirstRecordId, """{ "number": "A-1" }"""),
            Record(SecondRecordId, """{ "number": "A-2" }""")));

        var before = ApplicationCompiler.Compile(withoutSeed.Path);
        var result = ApplicationCompiler.Compile(withSeed.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var seed = Assert.Single(result.Model.Seeds);
        Assert.Equal(
            (Guid.Parse("77777777-7777-4777-8777-777777777771"), "Orders", "seeds/orders.json"),
            (seed.Id, seed.Name, seed.File));
        Assert.Equal(new EntityReference(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Order"), seed.Entity);
        Assert.Equal([Guid.Parse(FirstRecordId), Guid.Parse(SecondRecordId)], seed.Records.Select(record => record.Id));
        Assert.Equal("A-2", seed.Records[1].Values.GetProperty("number").GetString());
        Assert.NotNull(before.ContentHash);
        Assert.NotNull(result.ContentHash);
        Assert.NotEqual(before.ContentHash, result.ContentHash);
    }

    [Fact]
    public void Seed_values_are_not_checked_at_compile_time()
    {
        using var folder = Folder().With("seeds/orders.json", Seed(
            "Orders",
            "77777777-7777-4777-8777-777777777771",
            "Order",
            Record(FirstRecordId, """{ "number": 5, "missing": true }""")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Seed_over_an_unknown_entity_is_reported_at_its_entity()
    {
        using var folder = Folder().With("seeds/orders.json", Seed(
            "Orders",
            "77777777-7777-4777-8777-777777777771",
            "Invoice",
            Record(FirstRecordId, """{ "number": "A-1" }""")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownSeedEntity, "seeds/orders.json", "/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Invoice'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse("77777777-7777-4777-8777-777777777771"), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Seed_over_an_entity_whose_file_was_not_loaded_is_not_reported_again()
    {
        using var folder = Folder()
            .With("entities/invoice.json", """{ "id": "88888888-8888-4888-8888-888888888881", "kind": "entity", "name": "Invoice", "formatVersion": 1, "fields": "none" }""")
            .With("seeds/invoices.json", Seed(
                "Invoices",
                "77777777-7777-4777-8777-777777777771",
                "Invoice",
                Record(FirstRecordId, "{}")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "entities/invoice.json"), (diagnostic.Code, diagnostic.File));
    }

    [Fact]
    public void Seed_record_id_used_by_an_earlier_seed_file_is_reported_at_the_later_record_naming_the_first_file()
    {
        // The second id differs only in letter case, so the ids are compared as UUIDs.
        using var folder = Folder()
            .With("seeds/a.json", Seed(
                "A",
                "77777777-7777-4777-8777-777777777771",
                "Order",
                Record(FirstRecordId, """{ "number": "A-1" }""")))
            .With("seeds/b.json", Seed(
                "B",
                "77777777-7777-4777-8777-777777777772",
                "Order",
                Record(SecondRecordId, """{ "number": "B-1" }"""),
                Record(FirstRecordId.ToUpperInvariant(), """{ "number": "B-2" }""")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateSeedRecordId, "seeds/b.json", "/records/1/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'seeds/a.json'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse("77777777-7777-4777-8777-777777777772"), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Seed_record_id_repeated_in_the_same_file_is_reported_at_the_later_record()
    {
        using var folder = Folder().With("seeds/orders.json", Seed(
            "Orders",
            "77777777-7777-4777-8777-777777777771",
            "Order",
            Record(FirstRecordId, """{ "number": "A-1" }"""),
            Record(FirstRecordId, """{ "number": "A-2" }""")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateSeedRecordId, "seeds/orders.json", "/records/1/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'seeds/orders.json'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Seed_record_without_values_is_a_schema_violation()
    {
        using var folder = Folder().With("seeds/orders.json", Seed(
            "Orders",
            "77777777-7777-4777-8777-777777777771",
            "Order",
            $$"""{ "id": "{{FirstRecordId}}" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "seeds/orders.json", "/records/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", PresentationCompilerTests.Order);

    private static string Seed(string name, string id, string entity, params string[] records) =>
        $$"""{ "id": "{{id}}", "kind": "seed", "name": "{{name}}", "formatVersion": 1, "entity": "{{entity}}", "records": [{{string.Join(", ", records)}}] }""";

    private static string Record(string id, string values) => $$"""{ "id": "{{id}}", "values": {{values}} }""";
}
