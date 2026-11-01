using Axis.Data.Naming;

namespace Axis.Integration.Tests;

/// <summary>
/// A process expression aggregates the rows of a collection on a referenced record, such as
/// <c>count(shipment.lines)</c>. A decision on that count takes its branch by how many lines the
/// request's shipment has, and a request with no shipment counts 0. The test activates a copy of
/// StepApp whose Request has the <c>shipment</c> field, so the schema change stays in this class's
/// database.
/// </summary>
public sealed class ProcessCollectionPathTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string CountLines = """
        {
          "id": "a3b7d1f2-48c6-4e0b-9d15-72e6c0a8f931",
          "kind": "process",
          "name": "CountLines",
          "formatVersion": 1,
          "entity": "Request",
          "start": "check",
          "steps": [
            {
              "name": "check",
              "type": "decision",
              "branches": [
                { "when": "count(shipment.lines) > 1", "next": "many" },
                { "when": "count(shipment.lines) == 0 and sum(shipment.lines, quantity) == 0", "next": "none" }
              ],
              "otherwise": "one"
            },
            { "name": "many", "type": "end" },
            { "name": "none", "type": "end" },
            { "name": "one", "type": "end" }
          ]
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_decision_on_the_count_of_the_lines_of_a_referenced_record_takes_the_branch_for_that_count()
    {
        var request = (await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "entities", "request.json"), CancellationToken))
            .Replace(
                "\"fields\": [",
                "\"fields\": [\n    { \"name\": \"shipment\", \"type\": \"reference\", \"target\": \"Shipment\" },",
                StringComparison.Ordinal);
        var (releaseId, model) = await fixture.ActivateCopyWithFilesAsync(
            ("entities/request.json", request),
            ("processes/count-lines.json", CountLines));
        Assert.True(model.TryGetEntity("Request", out var entity));
        var twoLines = await fixture.InsertShipmentAsync(("A", 1), ("B", 2));
        var oneLine = await fixture.InsertShipmentAsync(("A", 1));
        var withTwo = await fixture.InsertRequestAsync(null, 1);
        var withOne = await fixture.InsertRequestAsync(null, 1);
        var withoutShipment = await fixture.InsertRequestAsync(null, 1);
        await SetShipmentAsync(entity.Id, withTwo, twoLines);
        await SetShipmentAsync(entity.Id, withOne, oneLine);

        var many = await fixture.StartAsync("CountLines", withTwo, releaseId, model);
        var one = await fixture.StartAsync("CountLines", withOne, releaseId, model);
        var none = await fixture.StartAsync("CountLines", withoutShipment, releaseId, model);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var withManyLines = await fixture.InstanceAsync(many);
        var withOneLine = await fixture.InstanceAsync(one);
        var withNoShipment = await fixture.InstanceAsync(none);
        Assert.Equal(("many", true), (withManyLines.Step, withManyLines.Ended));
        Assert.Equal(("one", true), (withOneLine.Step, withOneLine.Ended));
        Assert.Equal(("none", true), (withNoShipment.Step, withNoShipment.Ended));
    }

    private async Task SetShipmentAsync(Guid entityId, Guid id, Guid shipment)
    {
        await using var command = fixture.DataSource.CreateCommand(
            $"UPDATE {EntityNaming.QualifiedTable(EntityNaming.Table(entityId))} SET {EntityNaming.Quote(EntityNaming.Column("shipment"))} = @shipment WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id");
        command.Parameters.AddWithValue("shipment", shipment);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
