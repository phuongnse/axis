using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A task step with a <c>deadline</c> queues a <c>task.deadline</c> work item, and the worker expires
/// the task when it runs and the task is still open. The <c>Expire</c> process of StepApp has a
/// deadline of one day to its <c>expired</c> end step. A test makes the item due itself.
/// </summary>
public sealed class TaskDeadlineTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string Approve = """{ "outcome": "approve" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reaching_a_task_step_with_a_deadline_queues_one_deadline_item_due_after_the_step_time()
    {
        var (task, _, instance) = await CreateTaskAsync("Expire");

        var item = Assert.Single(await ItemsAsync(task));
        Assert.Equal(("task.deadline", instance), (item.Kind, item.Instance));
        Assert.Equal(
            1L,
            await CountAsync("SELECT count(*) FROM axis.process_work_items WHERE task_id = @id AND due_at = (SELECT created_at + interval '1 day' FROM axis.process_tasks WHERE id = @id)", task));
        Assert.Null(await fixture.ScalarAsync(TenantA, "SELECT expired_at FROM axis.process_tasks WHERE id = @id", ("id", task)));

        // The deadline is not due yet, so the worker leaves the task open.
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        Assert.Equal(ProcessTasks.Open, await StateAsync(task));
        Assert.Single(await ItemsAsync(task));
    }

    [Fact]
    public async Task A_task_step_without_a_deadline_queues_no_deadline_item()
    {
        var (task, _, instance) = await CreateTaskAsync("Decide");

        Assert.Empty(await ItemsAsync(task));
        Assert.Equal(
            0L,
            await CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = 'task.deadline'", instance));
    }

    [Fact]
    public async Task The_due_deadline_expires_the_open_task_and_the_instance_moves_to_next()
    {
        var (task, subject, instance) = await CreateTaskAsync("Expire");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = fixture.CreateClient().SignedInAs("maria");
        Assert.Contains(task, await InboxAsync(maria));
        await MakeDueAsync(task);

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(ProcessTasks.Expired, await StateAsync(task));
        Assert.NotNull(await fixture.ScalarAsync(TenantA, "SELECT expired_at FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Null(await fixture.ScalarAsync(TenantA, "SELECT outcome FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Empty(await ItemsAsync(task));
        var ended = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((ProcessStarts.Completed, "expired", ProcessTasks.Expired, 0L), (ended.State, ended.Step, ended.TaskState, ended.WorkItems));

        // The task step has one row for the deadline, next to the row of its creation and the end step's.
        var history = await fixture.HistoryAsync(TenantA, instance);
        Assert.Equal(["decide", "decide", "expired"], history.Select(row => row.Step));
        var expired = Assert.Single(history, row => row.Decision == "deadline");
        Assert.Equal(waiting.Revision, expired.Revision);
        Assert.Null(expired.Error);
        Assert.True(expired.FinishedAfterStart);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "subjectId": "{{subject:D}}", "subjectVersion": null }"""),
            JsonNode.Parse(expired.Input)));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "next": "expired" }"""),
            JsonNode.Parse(expired.Output!)));

        var (actor, actorName, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskExpired));
        Assert.Equal((AuditActors.System, null), (actor, actorName));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "step": "decide", "next": "expired" }"""),
            JsonNode.Parse(details)));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));

        Assert.DoesNotContain(task, await InboxAsync(maria));
        using var completed = await CompleteAsync(maria, task, Approve);
        using var problem = await RecordApiFixture.ReadProblemAsync(completed, HttpStatusCode.Conflict);
        Assert.Equal(ProcessTasks.Expired, await StateAsync(task));
    }

    [Fact]
    public async Task Completing_the_task_first_deletes_its_deadline_item()
    {
        var (task, _, instance) = await CreateTaskAsync("Expire");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        Assert.Single(await ItemsAsync(task));

        using (var response = await CompleteAsync(maria, task, Approve))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(await ItemsAsync(task));
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(ProcessTasks.Completed, await StateAsync(task));
        var ended = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((ProcessStarts.Completed, "approved"), (ended.State, ended.Step));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskExpired));
    }

    [Fact]
    public async Task A_deadline_item_that_finds_the_task_closed_changes_nothing()
    {
        var (task, _, instance) = await CreateTaskAsync("Expire");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using (var response = await CompleteAsync(maria, task, Approve))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        var before = await fixture.SnapshotAsync(TenantA, instance);
        await fixture.ExecuteAsync(
            TenantA,
            "INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id, task_id) VALUES (gen_random_uuid(), @tenant, 'task.deadline', now() - interval '1 minute', @instance, @task)",
            ("tenant", TenantA),
            ("instance", instance),
            ("task", task));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Empty(await ItemsAsync(task));
        Assert.Equal(ProcessTasks.Completed, await StateAsync(task));
        Assert.Equal(before, await fixture.SnapshotAsync(TenantA, instance));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskExpired));
    }

    [Fact]
    public async Task A_completion_that_holds_the_task_lock_when_the_deadline_runs_leaves_one_outcome()
    {
        var (task, _, instance) = await CreateTaskAsync("Expire");
        await MakeDueAsync(task);

        await using var holder = await fixture.OpenConnectionAsync(TenantA);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken);
        var locked = await TaskQueries.LockAsync(transaction, task, CancellationToken);
        Assert.NotNull(locked);

        // The deadline handler claims the item and waits for the task lock, which the completion holds.
        var run = Task.Run(() => fixture.RunUntilIdleAsync(TenantA, CancellationToken), CancellationToken);
        await WaitUntilAsync(
            async () => run.IsCompleted || await WaitingSessionsAsync() == 1,
            "The deadline handler did not wait on the task lock.");
        Assert.False(run.IsCompleted);

        Assert.True(await ProcessTasks.TryCompleteAsync(transaction, task, "approve", "maria", CancellationToken));
        await ProcessTasks.DeletePendingItemsAsync(transaction, task, CancellationToken);
        await ProcessTasks.ResumeAsync(
            transaction,
            locked,
            new TaskCompletion("approve", "approved", "maria", "Maria Manager", null, null, []),
            TenantA,
            CancellationToken);
        await transaction.CommitAsync(CancellationToken);
        await run;

        Assert.Equal(ProcessTasks.Completed, await StateAsync(task));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskExpired));
        Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));
        var history = await fixture.HistoryAsync(TenantA, instance);
        Assert.Equal("approve", Assert.Single(history, row => row.Step == "decide" && row.Decision is not null).Decision);
        Assert.Empty(await ItemsAsync(task));
        var ended = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal((ProcessStarts.Completed, "approved"), (ended.State, ended.Step));
    }

    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string process)
    {
        var department = await fixture.InsertDepartmentAsync(TenantA, "maria");
        var subject = await fixture.InsertRequestAsync(TenantA, department);
        var instance = await fixture.StartAsync(TenantA, process, subject);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        return (await fixture.TaskIdAsync(TenantA, instance), subject, instance);
    }

    private Task<int> MakeDueAsync(Guid task) =>
        fixture.ExecuteAsync(TenantA, "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE task_id = @id", ("id", task));

    private async Task<string> StateAsync(Guid task) =>
        (string)(await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.process_tasks WHERE id = @id", ("id", task)))!;

    private async Task<long> CountAsync(string sql, Guid id) =>
        (long)(await fixture.ScalarAsync(TenantA, sql, ("id", id)))!;

    private async Task<IReadOnlyList<(string Kind, Guid? Instance)>> ItemsAsync(Guid task)
    {
        await using var connection = await fixture.OpenConnectionAsync(TenantA);
        await using var command = new Npgsql.NpgsqlCommand("SELECT kind, process_instance_id FROM axis.process_work_items WHERE task_id = @id", connection);
        command.Parameters.AddWithValue("id", task);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(string, Guid?)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1)));
        }

        return rows;
    }

    private Task<long> WaitingSessionsAsync() =>
        fixture.AdminCountAsync(
            TenantA, "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'");

    private static async Task<List<Guid>> InboxAsync(HttpClient client)
    {
        using var request = RecordApiFixture.Request($"{Tasks}?pageSize=100", Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid task, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(TenantA), body);
        return await client.SendAsync(request, CancellationToken);
    }
}
