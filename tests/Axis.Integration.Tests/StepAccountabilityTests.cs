using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Integration.Tests;

/// <summary>
/// A step's <c>set</c> can write <c>accountableUser()</c> and <c>decisionTask()</c>, the instance's
/// stored accountable user and deciding task. The tests activate a copy of StepApp whose Request has
/// the optional text fields <c>decidedBy</c> and <c>decidingTask</c>, so the schema change stays in
/// this class's database.
/// </summary>
public sealed class StepAccountabilityTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string Stamp = """
        {
          "id": "3c9e7a52-8d14-4b6f-a1e0-72f5d0b4c8a6",
          "kind": "process",
          "name": "Stamp",
          "formatVersion": 1,
          "entity": "Request",
          "start": "stampBefore",
          "steps": [
            {
              "name": "stampBefore",
              "type": "operation",
              "operation": "updateRecord",
              "set": { "decidedBy": "accountableUser()", "decidingTask": "decisionTask()" },
              "next": "review"
            },
            {
              "name": "review",
              "type": "task",
              "label": { "textKey": "request.approve" },
              "assignee": { "role": "finance" },
              "form": "RequestReview",
              "outcomes": [{ "name": "approve", "label": { "textKey": "task.approve" }, "next": "stampAfter" }]
            },
            {
              "name": "stampAfter",
              "type": "operation",
              "operation": "updateRecord",
              "set": { "decidedBy": "accountableUser()", "decidingTask": "decisionTask()", "amount": "amount / divisor" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_step_writes_the_starter_before_any_task_and_the_completing_user_and_task_after_one()
    {
        var (releaseId, model) = await ActivateAsync();
        var request = await fixture.InsertRequestAsync(10m, 1);
        var instance = await fixture.StartAsync("Stamp", request, releaseId, model, "requester");

        await fixture.RunUntilIdleAsync(CancellationToken);

        var task = Assert.Single(await fixture.TasksAsync(instance)).Id;
        Assert.Equal("review", (await fixture.InstanceAsync(instance)).Step);
        Assert.Equal(("requester", null), await StampsAsync(model, request));

        await fixture.CompleteTaskAsync(instance, "approve", "stampAfter", "jordan", CancellationToken);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var finished = await fixture.InstanceAsync(instance);
        Assert.Equal(("done", true), (finished.Step, finished.Ended));
        Assert.Equal(("jordan", task.ToString("D")), await StampsAsync(model, request));
    }

    [Fact]
    public async Task A_retried_step_writes_the_completing_user_and_task_and_not_the_operator()
    {
        var (releaseId, model) = await ActivateAsync();
        var request = await fixture.InsertRequestAsync(10m, 0);
        var instance = await fixture.StartAsync("Stamp", request, releaseId, model, "requester");
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = Assert.Single(await fixture.TasksAsync(instance)).Id;
        await fixture.CompleteTaskAsync(instance, "approve", "stampAfter", "jordan", CancellationToken);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("failed", (await fixture.InstanceAsync(instance)).State);
        Assert.Equal(("requester", null), await StampsAsync(model, request));

        await fixture.SetDivisorAsync(request, 1);
        await fixture.RetryAsync("Stamp", instance, model, CancellationToken);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var finished = await fixture.InstanceAsync(instance);
        Assert.Equal(("completed", "done"), (finished.State, finished.Step));
        Assert.Equal(("jordan", task.ToString("D")), await StampsAsync(model, request));
    }

    private async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync()
    {
        var request = (await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "entities", "request.json"), CancellationToken))
            .Replace(
                "\"fields\": [",
                "\"fields\": [\n    { \"name\": \"decidedBy\", \"type\": \"text\" },\n    { \"name\": \"decidingTask\", \"type\": \"text\" },",
                StringComparison.Ordinal);
        return await fixture.ActivateCopyWithFilesAsync(
            ("entities/request.json", request),
            ("processes/stamp.json", Stamp));
    }

    /// <summary>The stored <c>decidedBy</c> and <c>decidingTask</c> of the request <paramref name="id"/>.</summary>
    private async Task<(string? DecidedBy, string? DecidingTask)> StampsAsync(ApplicationModel model, Guid id)
    {
        Assert.True(model.TryGetEntity("Request", out var entity));
        await using var command = fixture.DataSource.CreateCommand(
            $"""
            SELECT {EntityNaming.Quote(EntityNaming.Column("decidedBy"))}, {EntityNaming.Quote(EntityNaming.Column("decidingTask"))}
            FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))}
            WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
