using System.Net;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A task step with <c>escalations</c> queues one <c>task.escalate</c> work item for each, and the
/// worker reassigns the task as <c>system</c> when an item runs and the task is still open. The
/// <c>Escalate</c> process of StepApp reassigns after one hour to the <c>reviewer</c> role, and
/// <c>EscalateUser</c> reassigns to the user its subject's department manager names. Both have a
/// deadline of one day. A test makes the items due itself.
/// </summary>
public sealed class TaskEscalationTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reaching_a_task_step_with_an_escalation_queues_its_item_before_the_deadline_item()
    {
        var (task, _, instance) = await CreateTaskAsync("Escalate", "maria");

        var items = await ItemsAsync(task);
        Assert.Equal(
            [("task.escalate", instance, 0), ("task.deadline", instance, null)],
            items.OrderBy(item => item.Seq).Select(item => (item.Kind, item.Instance, item.Escalation)));
        Assert.Equal(
            1L,
            (long)(await fixture.ScalarAsync(
                TenantA,
                "SELECT count(*) FROM axis.process_work_items WHERE task_id = @id AND kind = 'task.escalate' AND due_at = (SELECT created_at + interval '1 hour' FROM axis.process_tasks WHERE id = @id)",
                ("id", task)))!);

        // The escalation is not due yet, so the worker leaves the task as it is.
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        Assert.Equal(("user", "maria"), await AssigneeAsync(task));
        Assert.Equal(2, (await ItemsAsync(task)).Count);
    }

    [Fact]
    public async Task A_due_escalation_reassigns_the_open_task_to_the_role_as_system_and_clears_the_claim()
    {
        var (task, subject, instance) = await CreateTaskAsync("Escalate", "maria");
        await fixture.ExecuteAsync(TenantA, "UPDATE axis.process_tasks SET claimed_by = 'maria', claimed_at = now() WHERE id = @id", ("id", task));
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        await MakeDueAsync(task, "task.escalate");

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(("role", "reviewer"), await AssigneeAsync(task));
        Assert.Null(await fixture.ScalarAsync(TenantA, "SELECT claimed_by FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Null(await fixture.ScalarAsync(TenantA, "SELECT claimed_at FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Equal(ProcessTasks.Open, await StateAsync(task));

        // The deadline item is still queued, and the instance still waits on the step.
        Assert.Equal(["task.deadline"], (await ItemsAsync(task)).Select(item => item.Kind));
        var after = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((ProcessStarts.Waiting, "decide", waiting.Revision), (after.State, after.Step, after.Revision));

        var (actor, actorName, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        Assert.Equal((AuditActors.System, null), (actor, actorName));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "from": { "user": "maria" }, "to": { "role": "reviewer" }, "escalation": 0 }"""),
            JsonNode.Parse(details)));

        var history = await fixture.HistoryAsync(TenantA, instance);
        Assert.Equal(["decide", "decide"], history.Select(row => row.Step));
        var escalated = Assert.Single(history, row => row.Output is not null && row.Output.Contains("escalation", StringComparison.Ordinal));
        Assert.Equal(waiting.Revision, escalated.Revision);
        Assert.Null(escalated.Decision);
        Assert.Null(escalated.Error);
        Assert.True(escalated.FinishedAfterStart);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "subjectId": "{{subject:D}}", "subjectVersion": null }"""),
            JsonNode.Parse(escalated.Input)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "escalation": 0 }"""), JsonNode.Parse(escalated.Output!)));
    }

    [Fact]
    public async Task A_due_escalation_reassigns_the_task_to_the_user_its_expression_gives()
    {
        var (task, _, instance) = await CreateTaskAsync("EscalateUser", "maria");
        await MakeDueAsync(task, "task.escalate");

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(("user", "maria"), await AssigneeAsync(task));
        var (actor, _, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        Assert.Equal(AuditActors.System, actor);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "from": { "role": "finance" }, "to": { "user": "maria" }, "escalation": 0 }"""),
            JsonNode.Parse(details)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task An_escalation_whose_user_expression_gives_no_user_leaves_the_task_as_it_is(string? manager)
    {
        var (task, _, instance) = await CreateTaskAsync("EscalateUser", manager);
        var before = await fixture.SnapshotAsync(TenantA, instance);
        await MakeDueAsync(task, "task.escalate");

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(("role", "finance"), await AssigneeAsync(task));
        Assert.Equal(ProcessTasks.Open, await StateAsync(task));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        Assert.Equal(["task.deadline"], (await ItemsAsync(task)).Select(item => item.Kind));
        var after = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((before.State, before.Step, before.HistoryRows, before.AuditRecords), (after.State, after.Step, after.HistoryRows, after.AuditRecords));
    }

    [Fact]
    public async Task Completing_the_task_deletes_its_escalation_items_and_a_stale_item_changes_nothing()
    {
        var (task, _, instance) = await CreateTaskAsync("Escalate", "maria");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        Assert.Equal(2, (await ItemsAsync(task)).Count);

        using (var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(TenantA), """{ "outcome": "approve" }"""))
        using (var response = await maria.SendAsync(request, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(await ItemsAsync(task));
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        var before = await fixture.SnapshotAsync(TenantA, instance);
        await fixture.ExecuteAsync(
            TenantA,
            "INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id, task_id, escalation) VALUES (gen_random_uuid(), @tenant, 'task.escalate', now() - interval '1 minute', @instance, @task, 0)",
            ("tenant", TenantA),
            ("instance", instance),
            ("task", task));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Empty(await ItemsAsync(task));
        Assert.Equal(ProcessTasks.Completed, await StateAsync(task));
        Assert.Equal(("user", "maria"), await AssigneeAsync(task));
        Assert.Equal(before, await fixture.SnapshotAsync(TenantA, instance));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
    }

    [Fact]
    public async Task A_notify_escalation_stores_the_role_notification_and_a_later_reassign_runs_as_system()
    {
        var (task, subject, instance) = await CreateTaskAsync("EscalateNotify", "maria");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal(
            [("task.escalate", 0), ("task.escalate", 1), ("task.deadline", null)],
            (await ItemsAsync(task)).OrderBy(item => item.Seq).Select(item => (item.Kind, item.Escalation)));
        await MakeEscalationDueAsync(task, 0);

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        var notification = await NotificationAsync(subject);
        Assert.Equal(("role", "reviewer", "request.notice.subject", "request.notice.body", "Tasks"), (notification.Kind, notification.Recipient, notification.Subject, notification.Body, notification.Page));
        Assert.Equal(fixture.ReleaseId(TenantA), notification.Release);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "title": "{{RequestTitle(subject)}}", "amount": null }"""),
            JsonNode.Parse(notification.Values)));
        Assert.Equal(("user", "maria"), await AssigneeAsync(task));
        Assert.Equal(ProcessTasks.Open, await StateAsync(task));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        var after = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((ProcessStarts.Waiting, "decide", waiting.Revision), (after.State, after.Step, after.Revision));
        var escalated = Assert.Single(await fixture.HistoryAsync(TenantA, instance), row => row.Output is not null && row.Output.Contains("escalation", StringComparison.Ordinal));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "escalation": 0, "notificationId": "{{notification.Id:D}}" }"""),
            JsonNode.Parse(escalated.Output!)));

        await MakeEscalationDueAsync(task, 1);

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(("role", "finance"), await AssigneeAsync(task));
        Assert.Equal(ProcessTasks.Open, await StateAsync(task));
        var (actor, _, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        Assert.Equal(AuditActors.System, actor);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "from": { "user": "maria" }, "to": { "role": "finance" }, "escalation": 1 }"""),
            JsonNode.Parse(details)));
        Assert.Equal(1L, (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notifications WHERE \"values\"->>'title' = @title", ("title", RequestTitle(subject))))!);
        Assert.Equal(["task.deadline"], (await ItemsAsync(task)).Select(item => item.Kind));
    }

    [Fact]
    public async Task A_notify_escalation_on_a_completed_task_stores_no_notification()
    {
        var (task, subject, instance) = await CreateTaskAsync("EscalateNotify", "maria");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using (var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(TenantA), """{ "outcome": "approve" }"""))
        using (var response = await maria.SendAsync(request, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        await fixture.ExecuteAsync(
            TenantA,
            "INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id, task_id, escalation) VALUES (gen_random_uuid(), @tenant, 'task.escalate', now() - interval '1 minute', @instance, @task, 0)",
            ("tenant", TenantA),
            ("instance", instance),
            ("task", task));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Empty(await ItemsAsync(task));
        Assert.Equal(ProcessTasks.Completed, await StateAsync(task));
        Assert.Equal(0L, (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notifications WHERE \"values\"->>'title' = @title", ("title", RequestTitle(subject))))!);
    }

    [Fact]
    public async Task The_deadline_that_expires_the_task_deletes_its_pending_escalation_items()
    {
        var (task, _, instance) = await CreateTaskAsync("Escalate", "maria");
        await fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE task_id = @id AND kind = 'task.deadline'",
            ("id", task));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(ProcessTasks.Expired, await StateAsync(task));
        Assert.Empty(await ItemsAsync(task));
        Assert.Equal(("user", "maria"), await AssigneeAsync(task));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        var ended = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((ProcessStarts.Completed, "expired"), (ended.State, ended.Step));
    }

    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string process, string? manager)
    {
        var department = await fixture.InsertDepartmentAsync(TenantA, manager);
        var subject = await fixture.InsertRequestAsync(TenantA, department);
        var instance = await fixture.StartAsync(TenantA, process, subject);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        return (await fixture.TaskIdAsync(TenantA, instance), subject, instance);
    }

    private Task<int> MakeDueAsync(Guid task, string kind) =>
        fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE task_id = @id AND kind = @kind",
            ("id", task),
            ("kind", kind));

    private Task<int> MakeEscalationDueAsync(Guid task, int index) =>
        fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE task_id = @id AND kind = 'task.escalate' AND escalation = @index",
            ("id", task),
            ("index", index));

    private async Task<(Guid Id, Guid Release, string Kind, string Recipient, string Subject, string Body, string Page, string Values)> NotificationAsync(Guid subject)
    {
        await using var connection = await fixture.OpenConnectionAsync(TenantA);
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT id, release_id, recipient_kind, recipient, subject_key, body_key, open_page, \"values\"::text FROM axis.notifications WHERE \"values\"->>'title' = @title",
            connection);
        command.Parameters.AddWithValue("title", RequestTitle(subject));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        var row = (reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));
        Assert.False(await reader.ReadAsync(CancellationToken));
        return row;
    }

    private async Task<string> StateAsync(Guid task) =>
        (string)(await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.process_tasks WHERE id = @id", ("id", task)))!;

    private async Task<(string Kind, string Assignee)> AssigneeAsync(Guid task)
    {
        await using var connection = await fixture.OpenConnectionAsync(TenantA);
        await using var command = new Npgsql.NpgsqlCommand("SELECT assignee_kind, assignee FROM axis.process_tasks WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", task);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<IReadOnlyList<(long Seq, string Kind, Guid? Instance, int? Escalation)>> ItemsAsync(Guid task)
    {
        await using var connection = await fixture.OpenConnectionAsync(TenantA);
        await using var command = new Npgsql.NpgsqlCommand("SELECT seq, kind, process_instance_id, escalation FROM axis.process_work_items WHERE task_id = @id", connection);
        command.Parameters.AddWithValue("id", task);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(long, string, Guid?, int?)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        return rows;
    }
}
