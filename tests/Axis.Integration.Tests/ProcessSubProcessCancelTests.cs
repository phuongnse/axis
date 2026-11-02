using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// A failed child leaves its parent <c>waiting</c>, and a retry of the child resumes the parent. A cancel
/// of a waiting child fails the parent's sub-process step. A cancel of the parent cancels its running and
/// waiting children in the same transaction, and is refused when a child has a call that may have reached
/// the other system. Each test activates a copy of StepApp with its own processes, so the tests of the
/// class do not meet on the unique index of a process and record.
/// </summary>
public sealed class ProcessSubProcessCancelTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string CloseAndFinish = """{ "name": "close", "type": "end" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failed_child_leaves_the_parent_waiting_and_a_retry_of_the_child_resumes_it_at_next()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/retry-parent.json", Parent("a7d6c9b8-b1d4-4270-8ca0-6e81df527f01", "RetryParent", "RetryChild")),
            ("processes/retry-child.json", """
                {
                  "id": "a7d6c9b8-b1d4-4270-8ca0-6e81df527f02",
                  "kind": "process",
                  "name": "RetryChild",
                  "formatVersion": 1,
                  "entity": "Request",
                  "start": "check",
                  "steps": [
                    {
                      "name": "check",
                      "type": "decision",
                      "branches": [{ "name": "branch1", "when": "round(amount / divisor, divisor) > 1", "next": "done" }],
                      "otherwise": "done"
                    },
                    { "name": "done", "type": "end" }
                  ]
                }
                """));
        var request = await fixture.InsertRequestAsync(10, 0);
        var parentId = await fixture.StartAsync("RetryParent", request, release, model);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var childId = await ChildIdAsync("RetryChild", model);
        Assert.Equal("failed", (await fixture.InstanceAsync(childId)).State);
        var waiting = await fixture.InstanceAsync(parentId);
        Assert.Equal(("waiting", "fulfil", false), (waiting.State, waiting.Step, waiting.Ended));

        await fixture.SetDivisorAsync(request, 2);
        await fixture.RetryAsync("RetryChild", childId, model, CancellationToken);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.InstanceAsync(childId)).State);
        var parent = await fixture.InstanceAsync(parentId);
        Assert.Equal(("completed", "close", true), (parent.State, parent.Step, parent.Ended));
        var resumed = JsonNode.Parse((await fixture.HistoryAsync(parentId)).Single(row => row.Next is not null).Output!)!.AsObject();
        Assert.Equal("close", (string?)resumed["next"]);
        Assert.Equal(childId, (Guid?)resumed["childInstanceId"]);
    }

    [Fact]
    public async Task Cancelling_a_waiting_child_fails_the_parents_step_with_a_fixed_error()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/childcancel-parent.json", Parent("b8e7dac9-c2e5-4381-9db1-7f92e0638a01", "ChildCancelParent", "ChildCancelChild")),
            ("processes/childcancel-child.json", TaskChild("b8e7dac9-c2e5-4381-9db1-7f92e0638a02", "ChildCancelChild")));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("ChildCancelParent", request, release, model);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var childId = await ChildIdAsync("ChildCancelChild", model);
        Assert.Equal("waiting", (await fixture.InstanceAsync(childId)).State);

        var result = await fixture.CancelAsync("ChildCancelChild", childId, model, CancellationToken);

        Assert.Equal(ProcessCancelOutcome.Cancelled, result.Outcome);
        Assert.Equal("cancelled", (await fixture.InstanceAsync(childId)).State);
        var parent = await fixture.InstanceAsync(parentId);
        Assert.Equal(("failed", "fulfil", true), (parent.State, parent.Step, parent.Ended));
        var history = await fixture.HistoryAsync(parentId);
        var failure = history[^1];
        Assert.Equal("fulfil", failure.Step);
        Assert.Equal("The sub-process of the step 'fulfil' was cancelled.", failure.Error);
        Assert.Equal(childId, (Guid?)JsonNode.Parse(failure.Output!)!["childInstanceId"]);
        Assert.Equal("system", await fixture.ScalarAsync(
            "SELECT actor FROM axis.audit_records WHERE process_instance_id = @id AND action = @action", ("id", parentId), ("action", AuditActions.ProcessFailed)));
        Assert.Equal(
            """{"step": "fulfil"}""",
            await fixture.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action", ("id", parentId), ("action", AuditActions.ProcessFailed)));
        Assert.Equal(1, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = @action", ("id", childId), ("action", AuditActions.ProcessCancelled)));
    }

    [Fact]
    public async Task Cancelling_the_parent_cancels_the_waiting_child_and_its_task_and_the_parent_is_not_failed()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/cascade-parent.json", Parent("c9f8ebda-d3f6-4492-8ec2-8a03f1749b01", "CascadeParent", "CascadeChild")),
            ("processes/cascade-child.json", TaskChild("c9f8ebda-d3f6-4492-8ec2-8a03f1749b02", "CascadeChild")));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("CascadeParent", request, release, model);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var childId = await ChildIdAsync("CascadeChild", model);
        Assert.Equal("open", Assert.Single(await fixture.TasksAsync(childId)).State);

        var result = await fixture.CancelAsync("CascadeParent", parentId, model, CancellationToken);

        Assert.Equal(ProcessCancelOutcome.Cancelled, result.Outcome);
        Assert.Equal("cancelled", (await fixture.InstanceAsync(parentId)).State);
        Assert.Equal("cancelled", (await fixture.InstanceAsync(childId)).State);
        Assert.Equal("cancelled", Assert.Single(await fixture.TasksAsync(childId)).State);
        foreach (var id in new[] { parentId, childId })
        {
            Assert.Equal(1, await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'maria' AND details->>'reason' = 'Fixed'",
                ("id", id),
                ("action", AuditActions.ProcessCancelled)));
            Assert.Equal(0, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", id)));
        }

        Assert.Equal(0, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = @action", ("id", parentId), ("action", AuditActions.ProcessFailed)));
        Assert.All(await fixture.HistoryAsync(parentId), row => Assert.Null(row.Error));
    }

    [Fact]
    public async Task A_child_with_an_unknown_call_stops_the_cancel_of_the_parent_and_nothing_changes()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/inflight-parent.json", Parent("d0a9fceb-e407-45a3-9fd3-9b14a285ac01", "InFlightParent", "InFlightChild")),
            ("processes/inflight-child.json", TaskChild("d0a9fceb-e407-45a3-9fd3-9b14a285ac02", "InFlightChild")));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("InFlightParent", request, release, model);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var childId = await ChildIdAsync("InFlightChild", model);
        var callId = Guid.CreateVersion7();
        await fixture.ScalarAsync(
            """
            INSERT INTO axis.connector_calls (id, process_instance_id, step, staged_revision, release_id, connector, operation, state, attempts)
            VALUES (@id, @instance, 'approve', 1, @release, 'Purchasing', 'createOrder', 'unknown', 0)
            """,
            ("id", callId),
            ("instance", childId),
            ("release", release));
        var before = await SnapshotAsync(parentId, childId);

        var result = await fixture.CancelAsync("InFlightParent", parentId, model, CancellationToken);

        Assert.Equal(ProcessCancelOutcome.CallInFlight, result.Outcome);
        Assert.Equal(callId, result.CallId);
        Assert.Equal(before, await SnapshotAsync(parentId, childId));
        Assert.Equal("waiting", (await fixture.InstanceAsync(childId)).State);
        Assert.Equal("open", Assert.Single(await fixture.TasksAsync(childId)).State);
    }

    // The states, revisions, task states, and the counts of history and audit rows of both instances.
    private async Task<string> SnapshotAsync(Guid parentId, Guid childId)
    {
        var parts = new List<string>();
        foreach (var id in new[] { parentId, childId })
        {
            var instance = await fixture.InstanceAsync(id);
            parts.Add($"{instance.State}/{instance.Revision}/{instance.Step}/{instance.Ended}");
            parts.AddRange((await fixture.TasksAsync(id)).Select(task => task.State));
            parts.Add((await fixture.HistoryAsync(id)).Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add((await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id", ("id", id))).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return string.Join('|', parts);
    }

    /// <summary>A process on Request named <paramref name="name"/> that runs <c>fulfil</c>, a sub-process step for <paramref name="child"/>, then ends.</summary>
    private static string Parent(string id, string name, string child) =>
        $$"""
        {
          "id": "{{id}}",
          "kind": "process",
          "name": "{{name}}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "fulfil",
          "steps": [
            { "name": "fulfil", "type": "subProcess", "process": "{{child}}", "next": "close" },
            {{CloseAndFinish}}
          ]
        }
        """;

    /// <summary>A process on Request named <paramref name="name"/> whose task step <c>approve</c> waits for the role <c>finance</c>.</summary>
    private static string TaskChild(string id, string name) =>
        $$"""
        {
          "id": "{{id}}",
          "kind": "process",
          "name": "{{name}}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "approve",
          "steps": [
            {
              "name": "approve",
              "type": "task",
              "label": { "textKey": "request.approve" },
              "assignee": { "role": "finance" },
              "form": "RequestReview",
              "outcomes": [{ "name": "approve", "label": { "textKey": "task.approve" }, "next": "done" }]
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private async Task<Guid> ChildIdAsync(string process, ApplicationModel model)
    {
        Assert.True(model.TryGetProcess(process, out var found));
        return (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_instances WHERE process_id = @process", ("process", found.Id)))!;
    }
}
