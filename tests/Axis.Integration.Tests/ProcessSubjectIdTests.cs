using Axis.Data.Naming;

namespace Axis.Integration.Tests;

/// <summary>
/// A process expression names <c>id</c>, the subject record's own id. A decision on
/// <c>id == previous</c> takes its branch by whether the request's <c>previous</c> is itself.
/// The test activates a copy of StepApp whose Request has the <c>previous</c> field, so the
/// schema change stays in this class's database.
/// </summary>
public sealed class ProcessSubjectIdTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string MatchPrevious = """
        {
          "id": "6f0a4c1e-3b52-4d7a-9a8e-5c1d2e7b9f40",
          "kind": "process",
          "name": "MatchPrevious",
          "formatVersion": 1,
          "entity": "Request",
          "start": "check",
          "steps": [
            { "name": "check", "type": "decision", "branches": [{ "name": "branch1", "when": "id == previous", "next": "same" }], "otherwise": "other" },
            { "name": "same", "type": "end" },
            { "name": "other", "type": "end" }
          ]
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_decision_on_the_id_takes_the_branch_for_the_subject_record()
    {
        var request = (await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "entities", "request.json"), CancellationToken))
            .Replace(
                "\"fields\": [",
                "\"fields\": [\n    { \"name\": \"previous\", \"type\": \"reference\", \"target\": \"Request\" },",
                StringComparison.Ordinal);
        var (releaseId, model) = await fixture.ActivateCopyWithFilesAsync(
            ("entities/request.json", request),
            ("processes/match-previous.json", MatchPrevious));
        Assert.True(model.TryGetEntity("Request", out var entity));
        var first = await fixture.InsertRequestAsync(null, 1);
        var second = await fixture.InsertRequestAsync(null, 1);
        await SetPreviousAsync(entity.Id, first, first);
        await SetPreviousAsync(entity.Id, second, first);

        var itself = await fixture.StartAsync("MatchPrevious", first, releaseId, model);
        var another = await fixture.StartAsync("MatchPrevious", second, releaseId, model);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var same = await fixture.InstanceAsync(itself);
        var other = await fixture.InstanceAsync(another);
        Assert.Equal(("same", true), (same.Step, same.Ended));
        Assert.Equal(("other", true), (other.Step, other.Ended));
    }

    private async Task SetPreviousAsync(Guid entityId, Guid id, Guid previous)
    {
        await using var command = fixture.DataSource.CreateCommand(
            $"UPDATE {EntityNaming.QualifiedTable(EntityNaming.Table(entityId))} SET {EntityNaming.Quote(EntityNaming.Column("previous"))} = @previous WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id");
        command.Parameters.AddWithValue("previous", previous);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
