using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class MigrationCompilerTests
{
    private const string FirstId = "7b3096e8-c14a-48f2-9a56-30bbd774e102";

    private const string SecondId = "7b3096e8-c14a-48f2-9a56-30bbd774e103";

    private const string Request = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "PurchaseRequest", "formatVersion": 1,
          "fields": [
            { "name": "title", "type": "text" },
            { "name": "reviewComments", "type": "text" },
            { "name": "priority", "type": "enum", "values": ["low", "normal", "high"], "required": true }
          ] }
        """;

    [Fact]
    public void The_sample_migration_compiles_into_the_model_in_path_order()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/purchase-request.json", Request)
            .With("migrations/0002-drop-legacy-code.json", Migration(
                "DropLegacyCode", SecondId, "PurchaseRequest", """[{ "type": "dropField", "field": "legacyCode" }]"""))
            .With("migrations/0001-rename-decision-comments.json", $$"""
                {
                  "id": "{{FirstId}}",
                  "kind": "migration",
                  "name": "RenameDecisionComments",
                  "formatVersion": 1,
                  "entity": "PurchaseRequest",
                  "changes": [
                    { "type": "renameField", "from": "decisionComments", "to": "reviewComments" },
                    { "type": "fill", "field": "priority", "value": "'normal'" }
                  ]
                }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(
            [
                (Guid.Parse(FirstId), "RenameDecisionComments", "migrations/0001-rename-decision-comments.json", "PurchaseRequest"),
                (Guid.Parse(SecondId), "DropLegacyCode", "migrations/0002-drop-legacy-code.json", "PurchaseRequest"),
            ],
            result.Model.Migrations.Select(migration => (migration.Id, migration.Name, migration.File, migration.Entity)));
        Assert.Equal(
            [
                new MigrationChangeModel(MigrationChangeType.RenameField, null, "decisionComments", "reviewComments", null),
                new MigrationChangeModel(MigrationChangeType.Fill, "priority", null, null, "'normal'"),
            ],
            result.Model.Migrations[0].Changes);
        Assert.Equal(
            [new MigrationChangeModel(MigrationChangeType.DropField, "legacyCode", null, null, null)],
            result.Model.Migrations[1].Changes);
        Assert.Contains(result.Resources, resource => resource.Path == "migrations/0001-rename-decision-comments.json");
    }

    [Fact]
    public void Migration_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder()
            .With("entities/drops.json", Entity("Drops", "22222222-2222-4222-8222-222222222221", """[{ "name": "x", "type": "text" }]"""))
            .With("entities/renames.json", Entity("Renames", "22222222-2222-4222-8222-222222222222", """[{ "name": "title", "type": "text" }]"""))
            .With("entities/fills.json", Entity("Fills", "22222222-2222-4222-8222-222222222223", """[{ "name": "title", "type": "text" }]"""))
            .With("entities/typed.json", Entity("Typed", "22222222-2222-4222-8222-222222222224", """[{ "name": "title", "type": "text" }]"""))
            .With("migrations/a.json", Migration("DropsOld", "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c01", "Drops", """
                [
                  { "type": "dropField", "field": "old" },
                  { "type": "renameField", "from": "old", "to": "x" }
                ]
                """))
            .With("migrations/b.json", Migration("RenamesTwice", "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c02", "Renames", """
                [
                  { "type": "renameField", "from": "a", "to": "title" },
                  { "type": "renameField", "from": "b", "to": "TITLE" }
                ]
                """))
            .With("migrations/c.json", Migration("FillsMissing", "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c03", "Fills", """
                [{ "type": "fill", "field": "missing", "value": "'a'" }]
                """))
            .With("migrations/d.json", Migration("FillsWrongType", "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c04", "Typed", """
                [{ "type": "fill", "field": "title", "value": "1" }]
                """))
            .With("migrations/e.json", Migration("ChangesNothing", "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c05", "Nothing", """
                [{ "type": "fill", "field": "title", "value": "'a'" }]
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownMigrationField, "migrations/a.json", "/changes/1/from"),
                (DiagnosticCodes.MigrationFieldAlreadyExists, "migrations/b.json", "/changes/1/to"),
                (DiagnosticCodes.MigrationFieldNotInEntity, "migrations/c.json", "/changes/0/field"),
                ("AXC0048", "migrations/d.json", "/changes/0/value"),
                (DiagnosticCodes.UnknownMigrationEntity, "migrations/e.json", "/entity"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Equal(
            ["AXC0171", "AXC0172", "AXC0173", "AXC0048", "AXC0170"],
            result.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_fill_with_a_function_outside_the_subset_is_reported_at_its_value()
    {
        using var folder = Folder().With("migrations/a.json", Migration("FillTitle", FirstId, "Note", """
            [{ "type": "fill", "field": "title", "value": "upper(title)" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("AXC0053", "/changes/0/value"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("Function 'upper' is not translated to SQL", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("convert")]
    public void An_aggregate_in_an_untyped_migration_value_is_reported_at_its_value(string type)
    {
        using var folder = Folder().With("migrations/a.json", Migration("CountLines", FirstId, "Note", $$"""
            [{ "type": "{{type}}", "field": "gone", "value": "count(lines) > 0" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "AXC0053");
        Assert.Equal("/changes/0/value", diagnostic.Path);
    }

    [Fact]
    public void A_convert_that_reads_its_own_field_is_still_checked_against_the_subset()
    {
        using var folder = Folder().With("migrations/a.json", Migration("ConvertTitle", FirstId, "Note", """
            [{ "type": "convert", "field": "title", "value": "upper(title)" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("AXC0053", "/changes/0/value"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_value_with_the_wrong_argument_count_is_a_diagnostic_when_it_is_not_type_checked()
    {
        using var folder = Folder().With("migrations/a.json", Migration("ConvertTitle", FirstId, "Note", """
            [{ "type": "convert", "field": "title", "value": "abs()" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("AXC0051", "/changes/0/value"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_fill_of_a_field_that_a_later_migration_drops_compiles()
    {
        using var folder = Folder()
            .With("migrations/0001-fill.json", Migration("FillNote", FirstId, "Note", """
                [{ "type": "fill", "field": "extra", "value": "title" }]
                """))
            .With("migrations/0002-drop.json", Migration("DropExtra", SecondId, "Note", """
                [{ "type": "dropField", "field": "extra" }]
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("fill", "'a'")]
    [InlineData("convert", "'a'")]
    public void A_fill_or_convert_after_a_drop_of_the_entity_is_not_checked_against_the_fields(string type, string value)
    {
        using var folder = Folder().With("migrations/a.json", Migration("DropAndReturn", FirstId, "Note", $$"""
            [
              { "type": "dropEntity" },
              { "type": "{{type}}", "field": "missing", "value": "{{value}}" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("TITLE")]
    public void A_rename_to_the_same_name_is_a_field_that_already_exists(string to)
    {
        using var folder = Folder().With("migrations/a.json", Migration("RenameTitle", FirstId, "Note", $$"""
            [{ "type": "renameField", "from": "title", "to": "{{to}}" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MigrationFieldAlreadyExists, "/changes/0/to"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_rename_without_a_destination_is_a_schema_violation()
    {
        using var folder = Folder().With("migrations/a.json", Migration("RenameTitle", FirstId, "Note", """
            [{ "type": "renameField", "from": "title" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "migrations/a.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path.StartsWith("/changes/0", StringComparison.Ordinal));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_migration_of_an_entity_that_is_dropped_is_not_reported_as_unknown()
    {
        using var folder = Folder().With("migrations/a.json", Migration("DropGone", FirstId, "Gone", """
            [{ "type": "fill", "field": "x", "value": "1" }, { "type": "dropEntity" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void An_entity_file_that_did_not_load_is_not_reported_again()
    {
        using var folder = Folder()
            .With("entities/broken.json", """
                { "id": "11111111-1111-4111-8111-111111111113", "kind": "entity", "name": "Broken", "formatVersion": 1, "fields": "none" }
                """)
            .With("migrations/a.json", Migration("FillBroken", FirstId, "Broken", """
                [{ "type": "fill", "field": "x", "value": "1" }]
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("entities/broken.json", diagnostic.File));
    }

    private static string Migration(string name, string id, string entity, string changes) => $$"""
        { "id": "{{id}}", "kind": "migration", "name": "{{name}}", "formatVersion": 1, "entity": "{{entity}}",
          "changes": {{changes}} }
        """;

    private static string Entity(string name, string id, string fields) => $$"""
        { "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1, "fields": {{fields}} }
        """;

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/note.json", Entity("Note", "11111111-1111-4111-8111-111111111112", """[{ "name": "title", "type": "text" }]"""));
}
