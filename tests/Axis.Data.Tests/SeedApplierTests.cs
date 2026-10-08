using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Seeding;
using Npgsql;

namespace Axis.Data.Tests;

public sealed class SeedApplierTests
{
    private static readonly EntityModel _note = Models.Entity(
        Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "Note",
        "entities/note.json",
        Models.Field("title", FieldType.Text, required: true),
        Models.Field("done", FieldType.Boolean));

    private static readonly EntityModel _step = Models.Entity(
        Guid.Parse("11111111-1111-4111-8111-111111111112"),
        "Step",
        "entities/step.json",
        Models.Field("text", FieldType.Text, required: true));

    private static readonly EntityModel _checklist = Models.Entity(
        Guid.Parse("11111111-1111-4111-8111-111111111113"),
        "Checklist",
        "entities/checklist.json",
        Models.Field("title", FieldType.Text, required: true),
        Models.Field("steps", FieldType.ChildCollection, target: _step));

    [Fact]
    public async Task Every_invalid_value_of_every_seed_is_reported_before_the_connection_is_used()
    {
        // The connection is never opened, so any database access would throw.
        await using var connection = new NpgsqlConnection();
        var model = Models.Application(_note) with
        {
            Seeds =
            [
                Seed("seeds/a.json", Record("""{ "title": "Valid" }"""), Record("""{ "title": 5 }""")),
                Seed("seeds/b.json", Record("""{ "done": false, "colour": "red" }""")),
            ],
        };

        var result = await SeedApplier.ApplyAsync(model, connection, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.InvalidSeedValue, diagnostic.Code));
        Assert.Equal(
            [
                ("seeds/a.json", "/records/1/values/title"),
                ("seeds/b.json", "/records/0/values/colour"),
                ("seeds/b.json", "/records/0/values/title"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public async Task Every_invalid_child_row_of_a_seed_is_reported_at_its_path_before_the_connection_is_used()
    {
        // The connection is never opened, so any database access would throw.
        await using var connection = new NpgsqlConnection();
        var model = Models.Application(_checklist, _step) with
        {
            Seeds =
            [
                Seed(
                    "seeds/checklists.json",
                    _checklist,
                    Record("""{ "title": "Valid", "steps": [{ "text": "One" }] }"""),
                    Record("""{ "title": "Invalid", "steps": [{ "text": "One" }, {}, 5] }""")),
            ],
        };

        var result = await SeedApplier.ApplyAsync(model, connection, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(
            ["/records/1/values/steps/1/text", "/records/1/values/steps/2"],
            result.Diagnostics.Select(diagnostic => diagnostic.Path));
    }

    private static SeedModel Seed(string file, params SeedRecordDefinition[] records) => Seed(file, _note, records);

    private static SeedModel Seed(string file, EntityModel entity, params SeedRecordDefinition[] records) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = entity.Name,
            File = file,
            Entity = new EntityReference(entity.Id, entity.Name),
            Records = records,
        };

    private static SeedRecordDefinition Record(string values) =>
        new(Guid.NewGuid(), JsonDocument.Parse(values).RootElement.Clone());
}
