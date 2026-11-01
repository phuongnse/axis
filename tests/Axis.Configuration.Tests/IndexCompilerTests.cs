using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class IndexCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    public static TheoryData<string, string, string, string> InvalidIndexes => new()
    {
        { """[{ "name": "index1", "fields": ["quantity", "status"] }, { "name": "index2", "fields": ["quantity", "status"] }]""", DiagnosticCodes.DuplicateIndex, "/indexes/1/fields", "'/indexes/0'" },
        // Field names match ignoring letter case, and the order of the fields matters only for being a duplicate.
        { """[{ "name": "index1", "fields": ["quantity", "status"], "unique": true }, { "name": "index2", "fields": ["QUANTITY", "Status"] }]""", DiagnosticCodes.DuplicateIndex, "/indexes/1/fields", "'/indexes/0'" },
        { """[{ "name": "index1", "fields": ["quantity", "nope"] }]""", DiagnosticCodes.UnknownIndexField, "/indexes/0/fields/1", "'nope'" },
        { """[{ "name": "index1", "fields": ["quantity"] }, { "name": "index2", "fields": ["lines"] }]""", DiagnosticCodes.InvalidIndexField, "/indexes/1/fields/0", "'child-collection'" },
        { """[{ "name": "index1", "fields": ["quantity", "receipt"] }]""", DiagnosticCodes.InvalidIndexField, "/indexes/0/fields/1", "'file'" },
        { """[{ "name": "index1", "fields": ["quantity", "tags"] }]""", DiagnosticCodes.InvalidIndexField, "/indexes/0/fields/1", "'references'" },
        { """[{ "name": "index1", "fields": [] }]""", DiagnosticCodes.SchemaViolation, "/indexes/0/fields", "" },
        { """[{ "name": "index1", "fields": ["quantity", "quantity"] }]""", DiagnosticCodes.SchemaViolation, "/indexes/0/fields", "" },
        { """[{ "unique": true }]""", DiagnosticCodes.SchemaViolation, "/indexes/0", "" },
        { """[{ "fields": ["quantity"] }]""", DiagnosticCodes.SchemaViolation, "/indexes/0", "" },
        { """[{ "name": "a", "fields": ["quantity"] }, { "name": "A", "fields": ["status"] }]""", DiagnosticCodes.DuplicateEntityElementName, "/indexes/1/name", "'/indexes/0'" },
    };

    [Theory]
    [MemberData(nameof(InvalidIndexes))]
    public void Invalid_index_is_reported_at_its_path_and_gives_no_model(string indexes, string code, string path, string messagePart)
    {
        using var folder = Folder(indexes);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "entities/order.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void More_than_32_fields_in_an_index_is_a_schema_violation()
    {
        var names = string.Join(", ", Enumerable.Range(0, 33).Select(position => $"\"f{position}\""));
        using var folder = Folder($$"""[{ "name": "index1", "fields": [{{names}}] }]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Code == DiagnosticCodes.SchemaViolation);
        Assert.Equal("/indexes/0/fields", diagnostic.Path);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Every_invalid_index_is_reported_at_its_own_position()
    {
        using var folder = Folder("""
            [
              { "name": "index1", "fields": ["quantity"] },
              { "name": "index2", "fields": ["missing", "lines"] },
              { "name": "index3", "fields": ["quantity"] }
            ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownIndexField, "/indexes/1/fields/0"),
                (DiagnosticCodes.InvalidIndexField, "/indexes/1/fields/1"),
                (DiagnosticCodes.DuplicateIndex, "/indexes/2/fields"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void Valid_indexes_are_in_the_entity_model_with_declared_field_names_and_change_the_content_hash()
    {
        using var withoutIndexes = Folder(null);
        // The same fields in another order are another index, and a reference is a field an index can hold.
        using var withIndexes = Folder("""
            [
              { "name": "index1", "fields": ["Quantity", "status"], "unique": true },
              { "name": "index2", "fields": ["status", "quantity"] },
              { "name": "index3", "fields": ["customer"], "unique": false }
            ]
            """);

        var before = ApplicationCompiler.Compile(withoutIndexes.Path);
        var result = ApplicationCompiler.Compile(withIndexes.Path);

        Assert.Empty(before.Diagnostics);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(
            [("quantity,status", true), ("status,quantity", false), ("customer", false)],
            order.Indexes.Select(index => (string.Join(',', index.Fields), index.Unique)));
        Assert.NotEqual(before.ContentHash, result.ContentHash);
    }

    private static TemporaryFolder Folder(string? indexes) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "quantity", "type": "integer" },
                    { "name": "status", "type": "enum", "values": ["open", "closed"] },
                    { "name": "lines", "type": "child-collection", "target": "OrderLine" },
                    { "name": "receipt", "type": "file" },
                    { "name": "tags", "type": "references", "target": "Customer" },
                    { "name": "customer", "type": "reference", "target": "Customer" }
                  ]{{(indexes is null ? "" : $", \"indexes\": {indexes}")}} }
                """)
            .With("entities/order-line.json", """
                { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "OrderLine", "formatVersion": 1,
                  "fields": [ { "name": "description", "type": "text" } ] }
                """)
            .With("entities/customer.json", """
                { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """);
}
