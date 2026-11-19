using System.Net;
using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class ProcessInstanceDetailTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string NotFoundTitle = "No instance of this process exists with this id.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failed_instance_shows_its_path_up_to_the_failed_step_with_the_stored_error()
    {
        var instance = await StartAndRunAsync(TenantA, "Break");

        using var body = await GetOkAsync(TenantA, "Break", instance);

        var root = body.RootElement;
        Assert.Equal(
            ["id", "process", "subjectEntity", "subjectId", "subjectLabel", "releaseId", "releaseNumber", "state", "step", "createdAt", "endedAt", "timerDueAt", "startedBy", "input", "result", "parent", "subProcesses", "graph", "path", "tasks", "calls", "commands", "studio"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(instance, root.GetProperty("id").GetGuid());
        Assert.Equal("Break", root.GetProperty("process").GetString());
        Assert.Equal("failed", root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("timerDueAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("parent").ValueKind);
        Assert.Empty(root.GetProperty("subProcesses").EnumerateArray());
        Assert.Equal(DateTimeKind.Utc, root.GetProperty("createdAt").GetDateTime().Kind);
        var path = Assert.Single(root.GetProperty("path").EnumerateArray());
        Assert.Equal(
            ["step", "startedAt", "finishedAt", "decision", "output", "error", "traceId"],
            path.EnumerateObject().Select(property => property.Name));
        Assert.Equal("lower", path.GetProperty("step").GetString());
        Assert.Equal("The record update was rejected: /values/amount: request.amountNegative", path.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, path.GetProperty("output").ValueKind);
        Assert.Equal(JsonValueKind.Null, path.GetProperty("decision").ValueKind);
        Assert.Empty(root.GetProperty("tasks").EnumerateArray());
        Assert.Empty(root.GetProperty("calls").EnumerateArray());
    }

    [Fact]
    public async Task A_waiting_instance_shows_its_open_task_with_the_assignee_kind_and_assignee()
    {
        var instance = await StartAndRunAsync(TenantA, "RoleApprove");
        var taskId = await fixture.TaskIdAsync(TenantA, instance);
        // PostgreSQL keeps microseconds.
        var due = DateTime.UtcNow.AddDays(2);
        var dueAt = new DateTime(due.Ticks - (due.Ticks % 10), DateTimeKind.Utc);
        await fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_tasks SET due_at = @due, claimed_by = 'lena', claimed_at = now() WHERE id = @id",
            ("due", dueAt),
            ("id", taskId));

        using var body = await GetOkAsync(TenantA, "RoleApprove", instance);

        var root = body.RootElement;
        Assert.Equal("waiting", root.GetProperty("state").GetString());
        Assert.Equal("approve", root.GetProperty("step").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("timerDueAt").ValueKind);
        var task = Assert.Single(root.GetProperty("tasks").EnumerateArray());
        Assert.Equal(
            ["id", "step", "assigneeKind", "assignee", "assigneeId", "state", "outcome", "completedBy", "completedById", "createdAt", "dueAt", "completedAt"],
            task.EnumerateObject().Select(property => property.Name));
        Assert.Equal(taskId, task.GetProperty("id").GetGuid());
        Assert.Equal("approve", task.GetProperty("step").GetString());
        Assert.Equal("role", task.GetProperty("assigneeKind").GetString());
        Assert.Equal("finance", task.GetProperty("assignee").GetString());
        Assert.Equal("lena", task.GetProperty("assigneeId").GetString());
        Assert.Equal(DateTimeKind.Utc, task.GetProperty("dueAt").GetDateTime().Kind);
        Assert.Equal(dueAt, task.GetProperty("dueAt").GetDateTime());
        Assert.Equal("open", task.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedById").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);

        var path = Assert.Single(root.GetProperty("path").EnumerateArray());
        Assert.Equal("approve", path.GetProperty("step").GetString());
        Assert.Equal("waiting", path.GetProperty("output").GetProperty("state").GetString());
        Assert.Equal(taskId, path.GetProperty("output").GetProperty("taskId").GetGuid());

        var graph = root.GetProperty("graph");
        Assert.Equal("approve", graph.GetProperty("start").GetString());
        var steps = graph.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["approve", "done"], steps.Select(step => step.GetProperty("name").GetString()));
        Assert.Equal(["name", "type", "labelKey", "event", "eventLabelKey", "transitions"], steps[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal("task", steps[0].GetProperty("type").GetString());
        Assert.Equal("request.approve", steps[0].GetProperty("labelKey").GetString());
        Assert.Equal([("done", "approve")], Transitions(steps[0]));
        var outcome = Assert.Single(steps[0].GetProperty("transitions").EnumerateArray());
        Assert.Equal(["next", "on", "branch", "labelKey", "after"], outcome.EnumerateObject().Select(property => property.Name));
        Assert.Equal("task.approve", outcome.GetProperty("labelKey").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("branch").ValueKind);
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("after").ValueKind);
        Assert.Equal(JsonValueKind.Null, steps[0].GetProperty("event").ValueKind);
        Assert.Equal(JsonValueKind.Null, steps[0].GetProperty("eventLabelKey").ValueKind);
        Assert.Equal("end", steps[1].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, steps[1].GetProperty("labelKey").ValueKind);
        Assert.Empty(steps[1].GetProperty("transitions").EnumerateArray());
    }

    [Fact]
    public async Task A_completed_instance_gives_its_end_step_and_the_values_its_steps_wrote_and_a_waiting_one_gives_none()
    {
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Submit", subject);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        using var record = await GetRecordAsync(TenantA, subject);
        var recorded = record.RootElement.GetProperty("values");

        using var body = await GetOkAsync(TenantA, "Submit", instance);

        Assert.Equal("completed", body.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("input").ValueKind);
        var result = body.RootElement.GetProperty("result");
        Assert.Equal(["step", "values"], result.EnumerateObject().Select(property => property.Name));
        Assert.Equal("done", result.GetProperty("step").GetString());
        var values = result.GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(["status", "submittedAt"], values.Select(value => value.GetProperty("name").GetString()));
        Assert.All(values, value =>
        {
            Assert.Equal(["name", "value", "hidden"], value.EnumerateObject().Select(property => property.Name));
            Assert.False(value.GetProperty("hidden").GetBoolean());
        });
        Assert.Equal("submitted", values[0].GetProperty("value").GetString());
        Assert.Equal(recorded.GetProperty("submittedAt").GetString(), values[1].GetProperty("value").GetString());

        // The record changes later, but the instance keeps the values it completed with.
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        await fixture.ExecuteAsync(
            TenantA,
            $"UPDATE {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} SET {EntityNaming.Column("status")} = 'draft' WHERE {EntityNaming.IdColumn} = @id",
            ("id", subject));
        using var later = await GetOkAsync(TenantA, "Submit", instance);
        Assert.Equal("submitted", later.RootElement.GetProperty("result").GetProperty("values")[0].GetProperty("value").GetString());

        var waiting = await StartAndRunAsync(TenantA, "RoleApprove");
        using var waitingBody = await GetOkAsync(TenantA, "RoleApprove", waiting);
        Assert.Equal("waiting", waitingBody.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, waitingBody.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task A_user_who_may_not_read_a_written_field_gets_that_value_as_null_and_hidden()
    {
        var instance = await StartAndRunAsync(TenantA, "Submit");

        using var body = await GetOkAsync(TenantA, "Submit", instance, "carl");

        var values = body.RootElement.GetProperty("result").GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(["status", "submittedAt"], values.Select(value => value.GetProperty("name").GetString()));
        Assert.All(values, value =>
        {
            Assert.Equal(JsonValueKind.Null, value.GetProperty("value").ValueKind);
            Assert.True(value.GetProperty("hidden").GetBoolean());
        });
        Assert.DoesNotContain("\"submitted\"", body.RootElement.GetProperty("result").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_instance_that_completed_before_the_result_was_kept_has_values_null()
    {
        var instance = await StartAndRunAsync(TenantA, "Submit");
        await fixture.ExecuteAsync(TenantA, "UPDATE axis.process_instances SET result = NULL WHERE id = @id", ("id", instance));

        using var body = await GetOkAsync(TenantA, "Submit", instance);

        var result = body.RootElement.GetProperty("result");
        Assert.Equal("done", result.GetProperty("step").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("values").ValueKind);
    }

    [Fact]
    public async Task A_completed_task_shows_the_name_and_the_id_of_the_person_who_completed_it()
    {
        var instance = await StartAndRunAsync(TenantA, "RoleApprove");
        var taskId = await fixture.TaskIdAsync(TenantA, instance);
        await fixture.InsertProfileAsync(TenantA, "maria", "Maria Manager");
        await fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_tasks SET state = 'completed', outcome = 'approve', completed_by = 'maria', completed_at = now() WHERE id = @id",
            ("id", taskId));

        using var body = await GetOkAsync(TenantA, "RoleApprove", instance);

        var task = Assert.Single(body.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.Equal("Maria Manager", task.GetProperty("completedBy").GetString());
        Assert.Equal("maria", task.GetProperty("completedById").GetString());
        Assert.Equal("finance", task.GetProperty("assignee").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("assigneeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("dueAt").ValueKind);
    }

    [Fact]
    public async Task A_user_assignee_shows_by_name_and_a_user_without_a_profile_shows_by_id()
    {
        var instance = await StartAndRunAsync(TenantA, "RoleApprove");
        var taskId = await fixture.TaskIdAsync(TenantA, instance);
        await fixture.InsertProfileAsync(TenantA, "fiona", "Fiona Finance");
        await fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_tasks SET assignee_kind = 'user', assignee = 'fiona', state = 'completed', outcome = 'approve', completed_by = 'nobody-with-a-profile', completed_at = now() WHERE id = @id",
            ("id", taskId));

        using var body = await GetOkAsync(TenantA, "RoleApprove", instance);

        var task = Assert.Single(body.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("user", task.GetProperty("assigneeKind").GetString());
        Assert.Equal("Fiona Finance", task.GetProperty("assignee").GetString());
        Assert.Equal("fiona", task.GetProperty("assigneeId").GetString());
        Assert.Equal("nobody-with-a-profile", task.GetProperty("completedBy").GetString());
        Assert.Equal("nobody-with-a-profile", task.GetProperty("completedById").GetString());
    }

    [Fact]
    public async Task A_reader_with_the_author_role_gets_the_studio_address_and_one_without_it_gets_null()
    {
        var instance = await StartAndRunAsync(TenantA, "RoleApprove");
        Assert.True(fixture.Model.TryGetProcess("RoleApprove", out var process));
        var folder = Directory.CreateTempSubdirectory("axis-studio-detail-");
        try
        {
            await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Studio:AuthorRole", "finance");
                builder.UseSetting("Studio:Folders:0", folder.FullName);
            });

            using var author = await GetOkAsync(factory, "lena", TenantA, "RoleApprove", instance);
            using var reader = await GetOkAsync(factory, "ines", TenantA, "RoleApprove", instance);

            var studio = author.RootElement.GetProperty("studio");
            Assert.Equal(["app", "resource"], studio.EnumerateObject().Select(property => property.Name));
            Assert.Equal("StepApp", studio.GetProperty("app").GetString());
            Assert.Equal(process.Id, studio.GetProperty("resource").GetGuid());
            Assert.Equal(JsonValueKind.Null, reader.RootElement.GetProperty("studio").ValueKind);
        }
        finally
        {
            folder.Delete(recursive: true);
        }

        // Studio is off in the default host, so even the user who holds the role has no address.
        using var off = await GetOkAsync(TenantA, "RoleApprove", instance, "lena");
        Assert.Equal(JsonValueKind.Null, off.RootElement.GetProperty("studio").ValueKind);
    }

    [Fact]
    public async Task A_waiting_timer_shows_its_due_time()
    {
        var instance = await StartAndRunAsync(TenantA, "TimerWait");
        var dueAt = Assert.IsType<DateTime>(await fixture.ScalarAsync(
            TenantA,
            "SELECT due_at FROM axis.process_work_items WHERE process_instance_id = @id AND kind = 'process.timer'",
            ("id", instance)));

        using var body = await GetOkAsync(TenantA, "TimerWait", instance);

        var root = body.RootElement;
        Assert.Equal("waiting", root.GetProperty("state").GetString());
        Assert.Equal("pause", root.GetProperty("step").GetString());
        Assert.Equal("Request", root.GetProperty("subjectEntity").GetString());
        var timerDueAt = root.GetProperty("timerDueAt").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, timerDueAt.Kind);
        Assert.Equal(dueAt.ToUniversalTime(), timerDueAt);
    }

    [Fact]
    public async Task A_timer_item_from_an_older_revision_is_not_shown()
    {
        var instance = await StartAndRunAsync(TenantA, "TimerWait");
        await fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_work_items SET revision = revision - 1 WHERE process_instance_id = @id AND kind = 'process.timer'",
            ("id", instance));

        using var body = await GetOkAsync(TenantA, "TimerWait", instance);

        var root = body.RootElement;
        Assert.Equal("waiting", root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("timerDueAt").ValueKind);
    }

    [Fact]
    public async Task After_a_newer_release_is_activated_the_graph_is_still_the_pinned_releases()
    {
        var instance = await StartAndRunAsync(TenantB, "RoleApprove");
        await fixture.ActivateEditedAsync(
            TenantB,
            (path, content) => path == Path.Combine("processes", "role-approve.json") ? content.Replace("\"done\"", "\"closed\"", StringComparison.Ordinal) : content);

        using var body = await GetOkAsync(TenantB, "RoleApprove", instance);

        var root = body.RootElement;
        Assert.Equal(fixture.ReleaseId(TenantB), root.GetProperty("releaseId").GetGuid());
        var steps = root.GetProperty("graph").GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["approve", "done"], steps.Select(step => step.GetProperty("name").GetString()));
        Assert.Equal([("done", "approve")], Transitions(steps[0]));
    }

    [Fact]
    public async Task A_decision_graph_names_each_branch_by_its_index_and_the_fallback_otherwise()
    {
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Divide", subject);

        using var body = await GetOkAsync(TenantA, "Divide", instance);

        var root = body.RootElement;
        var steps = root.GetProperty("graph").GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal("check", root.GetProperty("graph").GetProperty("start").GetString());
        Assert.Equal("decision", steps[0].GetProperty("type").GetString());
        Assert.Equal([("done", "0"), ("done", "otherwise")], Transitions(steps[0]));
        Assert.Equal("request.check", steps[0].GetProperty("labelKey").GetString());
        var transitions = steps[0].GetProperty("transitions").EnumerateArray().ToList();
        Assert.Equal("branch1", transitions[0].GetProperty("branch").GetString());
        Assert.Equal("request.check.divided", transitions[0].GetProperty("labelKey").GetString());
        Assert.Equal(JsonValueKind.Null, transitions[1].GetProperty("branch").ValueKind);
        Assert.Equal(JsonValueKind.Null, transitions[1].GetProperty("labelKey").ValueKind);
        Assert.Equal("end", steps[1].GetProperty("type").GetString());
        Assert.Empty(steps[1].GetProperty("transitions").EnumerateArray());
        Assert.Empty(root.GetProperty("path").EnumerateArray());
        Assert.Empty(root.GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task A_wait_event_graph_gives_its_event_and_the_timeout_after()
    {
        var instance = await StartAndRunAsync(TenantA, "WaitEvent");

        using var body = await GetOkAsync(TenantA, "WaitEvent", instance);

        var steps = body.RootElement.GetProperty("graph").GetProperty("steps").EnumerateArray().ToList();
        var wait = steps[0];
        Assert.Equal("waitEvent", wait.GetProperty("type").GetString());
        Assert.Equal("request.await", wait.GetProperty("labelKey").GetString());
        Assert.Equal("RequestNoted", wait.GetProperty("event").GetString());
        Assert.Equal(JsonValueKind.Null, wait.GetProperty("eventLabelKey").ValueKind);
        Assert.Equal([("done", (string?)null), ("timedOut", "timeout")], Transitions(wait));
        var transitions = wait.GetProperty("transitions").EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, transitions[0].GetProperty("after").ValueKind);
        Assert.Equal("PT1H", transitions[1].GetProperty("after").GetString());
        Assert.Equal(JsonValueKind.Null, steps[1].GetProperty("event").ValueKind);
    }

    [Fact]
    public async Task A_parent_lists_its_sub_processes_and_each_sub_process_names_its_parent()
    {
        var parent = await StartAndRunAsync(TenantA, "RoleApprove");
        var first = await StartChildAsync(TenantA, "Decide", parent, "fulfil");
        var second = await StartChildAsync(TenantA, "Decide", parent, "fulfil");
        await fixture.ExecuteAsync(
            TenantA,
            "UPDATE axis.process_instances SET state = 'failed', ended_at = now() WHERE id = @id",
            ("id", second));

        using var body = await GetOkAsync(TenantA, "RoleApprove", parent);

        var items = body.RootElement.GetProperty("subProcesses").EnumerateArray().ToList();
        Assert.Equal([first, second], items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.All(
            items,
            item =>
            {
                Assert.Equal(
                    ["id", "process", "step", "state", "createdAt", "endedAt"],
                    item.EnumerateObject().Select(property => property.Name));
                Assert.Equal("Decide", item.GetProperty("process").GetString());
                Assert.Equal("fulfil", item.GetProperty("step").GetString());
                Assert.Equal(DateTimeKind.Utc, item.GetProperty("createdAt").GetDateTime().Kind);
            });
        Assert.Equal(["running", "failed"], items.Select(item => item.GetProperty("state").GetString()));
        Assert.Equal(JsonValueKind.Null, items.Single(item => item.GetProperty("id").GetGuid() == first).GetProperty("endedAt").ValueKind);
        Assert.Equal(JsonValueKind.String, items.Single(item => item.GetProperty("id").GetGuid() == second).GetProperty("endedAt").ValueKind);

        using var child = await GetOkAsync(TenantA, "Decide", first);
        var named = child.RootElement.GetProperty("parent");
        Assert.Equal(["id", "process", "step"], named.EnumerateObject().Select(property => property.Name));
        Assert.Equal(parent, named.GetProperty("id").GetGuid());
        Assert.Equal("RoleApprove", named.GetProperty("process").GetString());
        Assert.Equal("fulfil", named.GetProperty("step").GetString());
        Assert.Empty(child.RootElement.GetProperty("subProcesses").EnumerateArray());

        // The child names its parent as its starter, with no action. An instance with no stored starter has none.
        var startedBy = child.RootElement.GetProperty("startedBy");
        Assert.Equal(["kind", "name", "id", "via"], startedBy.EnumerateObject().Select(property => property.Name));
        Assert.Equal("parent", startedBy.GetProperty("kind").GetString());
        Assert.Equal("RoleApprove", startedBy.GetProperty("name").GetString());
        Assert.Equal(parent.ToString(), startedBy.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, startedBy.GetProperty("via").ValueKind);
        using var root = await GetOkAsync(TenantA, "RoleApprove", parent);
        Assert.Equal(JsonValueKind.Null, root.RootElement.GetProperty("startedBy").ValueKind);
    }

    [Fact]
    public async Task A_sub_process_of_a_process_the_user_may_not_inspect_is_left_out()
    {
        var parent = await StartAndRunAsync(TenantA, "RoleApprove");
        var child = await StartChildAsync(TenantA, "Rework", parent, "fulfil");

        using var hidden = await GetOkAsync(TenantA, "RoleApprove", parent, "ines");
        using var shown = await GetOkAsync(TenantA, "RoleApprove", parent, "lena");

        Assert.Empty(hidden.RootElement.GetProperty("subProcesses").EnumerateArray());
        var item = Assert.Single(shown.RootElement.GetProperty("subProcesses").EnumerateArray());
        Assert.Equal(child, item.GetProperty("id").GetGuid());
        Assert.Equal("Rework", item.GetProperty("process").GetString());
    }

    [Fact]
    public async Task A_parent_of_a_process_the_user_may_not_inspect_shows_as_null()
    {
        var parent = await fixture.StartAsync(TenantA, "Rework", await fixture.InsertRequestAsync(TenantA, null));
        var child = await StartChildAsync(TenantA, "Decide", parent, "fulfil");

        using var hidden = await GetOkAsync(TenantA, "Decide", child, "ines");
        using var shown = await GetOkAsync(TenantA, "Decide", child, "lena");

        Assert.Equal(JsonValueKind.Null, hidden.RootElement.GetProperty("parent").ValueKind);
        var hiddenStartedBy = hidden.RootElement.GetProperty("startedBy");
        Assert.Equal("parent", hiddenStartedBy.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, hiddenStartedBy.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, hiddenStartedBy.GetProperty("id").ValueKind);
        var shownStartedBy = shown.RootElement.GetProperty("startedBy");
        Assert.Equal("RoleApprove", shownStartedBy.GetProperty("name").GetString()); // the name StartChildAsync stores
        Assert.Equal(parent.ToString("D"), shownStartedBy.GetProperty("id").GetString());
        Assert.Equal(parent, shown.RootElement.GetProperty("parent").GetProperty("id").GetGuid());
        Assert.Equal("Rework", shown.RootElement.GetProperty("parent").GetProperty("process").GetString());
    }

    [Fact]
    public async Task An_instance_the_user_cannot_see_is_one_404_whatever_the_reason()
    {
        var breakInstance = await StartAndRunAsync(TenantA, "Break");
        var department = await fixture.InsertDepartmentAsync(TenantA, "maria");
        var hiddenSubject = await fixture.InsertRequestAsync(TenantA, department);
        var hidden = await fixture.StartAsync(TenantA, "RoleApprove", hiddenSubject);
        var deletedSubject = await fixture.InsertRequestAsync(TenantA, null);
        var deleted = await fixture.StartAsync(TenantA, "RoleApprove", deletedSubject);
        await fixture.DeleteRequestAsync(TenantA, deletedSubject);

        var requests = new[]
        {
            ("RoleApprove", breakInstance.ToString("D")),
            ("RoleApprove", hidden.ToString("D")),
            ("RoleApprove", deleted.ToString("D")),
            ("RoleApprove", Guid.NewGuid().ToString("D")),
            ("RoleApprove", "abc"),
            ("RoleApprove", breakInstance.ToString("N")),
        };
        foreach (var (process, id) in requests)
        {
            using var response = await GetAsync("ines", TenantA, process, id);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
            Assert.Equal(NotFoundTitle, problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task A_user_with_no_inspect_grant_gets_403_even_when_they_may_read_the_subject()
    {
        var instance = await StartAndRunAsync(TenantA, "Break");

        using var response = await GetAsync("rita", TenantA, "Break", instance.ToString("D"));

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Without_a_session_the_detail_is_401()
    {
        var instance = await StartAndRunAsync(TenantA, "Break");
        using var client = fixture.CreateClient();

        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/Break/instances/{instance:D}", Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_connector_call_lists_its_attempts_and_lookups_in_start_order()
    {
        var (instance, call) = await StartWithCallAsync(TenantA, """{"reference":"Sales","amount":500}""");
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        await fixture.InsertCallAttemptAsync(TenantA, call, "attempt", 2, start.AddSeconds(4), "succeeded", 200);
        await fixture.InsertCallAttemptAsync(TenantA, call, "attempt", 1, start, "retryable", 503);
        await fixture.InsertCallAttemptAsync(TenantA, call, "lookup", 1, start.AddSeconds(2), "notFound", 404);

        using var body = await GetOkAsync(TenantA, "DepartmentOrder", instance);

        var calls = body.RootElement.GetProperty("calls").EnumerateArray().ToList();
        var item = Assert.Single(calls);
        Assert.Equal(
            ["id", "kind", "step", "connector", "operation", "state", "attempts", "input", "inputHidden", "resolvable"],
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal(call, item.GetProperty("id").GetGuid());
        Assert.Equal("connector", item.GetProperty("kind").GetString());
        Assert.Equal("createOrder", item.GetProperty("step").GetString());
        Assert.Equal("Purchasing", item.GetProperty("connector").GetString());
        Assert.Equal("createOrder", item.GetProperty("operation").GetString());
        Assert.Equal("pending", item.GetProperty("state").GetString());
        var attempts = item.GetProperty("attempts").EnumerateArray().ToList();
        Assert.Equal(
            [("attempt", 1, "retryable", 503), ("lookup", 1, "notFound", 404), ("attempt", 2, "succeeded", 200)],
            attempts.Select(attempt => (
                attempt.GetProperty("kind").GetString()!,
                attempt.GetProperty("number").GetInt32(),
                attempt.GetProperty("outcome").GetString()!,
                attempt.GetProperty("httpStatus").GetInt32())));
        Assert.Equal(
            ["kind", "number", "startedAt", "finishedAt", "outcome", "httpStatus", "error", "traceId"],
            attempts[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, attempts[0].GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Null, attempts[0].GetProperty("traceId").ValueKind);
        Assert.Equal(DateTimeKind.Utc, attempts[0].GetProperty("startedAt").GetDateTime().Kind);
        Assert.True(attempts[0].GetProperty("startedAt").GetDateTime() < attempts[1].GetProperty("startedAt").GetDateTime());
    }

    [Fact]
    public async Task A_user_who_may_not_read_a_field_on_a_reference_path_gets_the_input_hidden()
    {
        var (instance, _) = await StartWithCallAsync(TenantA, """{"reference":"Sales","amount":500}""");

        using var body = await GetOkAsync(TenantA, "DepartmentOrder", instance);

        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
        Assert.DoesNotContain("Sales", call.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_who_may_read_every_mapped_field_including_the_output_target_sees_the_input()
    {
        var (instance, _) = await StartWithCallAsync(TenantA, """{"reference":"Sales","amount":500}""");

        using var body = await GetOkAsync(TenantA, "DepartmentOrder", instance, "ada");

        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal("Sales", call.GetProperty("input").GetProperty("reference").GetString());
        Assert.Equal(500, call.GetProperty("input").GetProperty("amount").GetDecimal());
        Assert.False(call.GetProperty("inputHidden").GetBoolean());
    }

    [Fact]
    public async Task A_user_who_may_not_read_the_output_target_field_gets_the_input_hidden()
    {
        var (instance, _) = await StartWithCallAsync(TenantA, """{"reference":"Sales","amount":500}""");

        using var body = await GetOkAsync(TenantA, "DepartmentOrder", instance, "carl");

        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
    }

    [Fact]
    public async Task Marking_an_existing_field_sensitive_without_data_keys_fails_and_changes_no_table()
    {
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        var before = await fixture.ReadTableAsync(TenantB, request.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.TryActivateEditedAsync(TenantB, MarkSensitive("amount")));

        Assert.Contains("data keys", error.Message, StringComparison.Ordinal);
        var after = await fixture.ReadTableAsync(TenantB, request.Id);
        Assert.Equal(before.Columns, after.Columns);
    }

    // Edits the Request entity so that each of the named fields declares "sensitive": true. A notify step may not
    // read a sensitive field, so the notify processes and the notify escalation of the app give the same values a literal instead.
    // A filter may not read one either, so the inspector policy's filter becomes "true".
    private static Func<string, string, string?> MarkSensitive(params string[] fields) =>
        (path, content) =>
        {
            if (path == Path.Combine("processes", "notify.json")
                || path == Path.Combine("processes", "notify-email.json")
                || path == Path.Combine("processes", "escalate-notify.json"))
            {
                return fields.Aggregate(content, (text, field) => text.Replace($"\"{field}\": \"{field}\"", $"\"{field}\": \"0\"", StringComparison.Ordinal));
            }

            if (path == Path.Combine("policies", "inspector-access.json"))
            {
                return content.Replace("\"filter\": \"department is null\"", "\"filter\": \"true\"", StringComparison.Ordinal);
            }

            if (path != Path.Combine("entities", "request.json"))
            {
                return content;
            }

            foreach (var field in fields)
            {
                var start = content.IndexOf($"{{ \"name\": \"{field}\"", StringComparison.Ordinal);
                Assert.True(start >= 0, $"The Request entity has no field {field}.");
                var end = content.IndexOf(" }", start, StringComparison.Ordinal);
                content = content.Insert(end, ", \"sensitive\": true");
            }

            return content;
        };

    private async Task<(Guid Instance, Guid Call)> StartWithCallAsync(string tenant, string inputJson)
    {
        var subject = await fixture.InsertRequestAsync(tenant, null);
        var instance = await fixture.StartAsync(tenant, "DepartmentOrder", subject);
        var call = await fixture.InsertConnectorCallAsync(tenant, instance, "createOrder", inputJson);
        return (instance, call);
    }

    private static List<(string? Next, string? On)> Transitions(JsonElement step) =>
        step.GetProperty("transitions").EnumerateArray()
            .Select(transition => (transition.GetProperty("next").GetString(), transition.GetProperty("on").GetString()))
            .ToList();

    // Starts an instance of the process on a new request and links it as a sub-process of the parent instance.
    // StepApp has no sub-process step, so the link is written as the engine would write it.
    private async Task<Guid> StartChildAsync(string tenant, string process, Guid parent, string step)
    {
        var subject = await fixture.InsertRequestAsync(tenant, null);
        var child = await fixture.StartAsync(tenant, process, subject);
        await fixture.ExecuteAsync(
            tenant,
            "UPDATE axis.process_instances SET parent_instance_id = @parent, parent_step = @step, started_by = @startedBy::jsonb WHERE id = @id",
            ("parent", parent),
            ("step", step),
            ("startedBy", ProcessStartedBy.Parent("RoleApprove", parent).ToJson()),
            ("id", child));
        return child;
    }

    private async Task<JsonDocument> GetRecordAsync(string tenant, Guid id)
    {
        using var client = fixture.CreateClient().SignedInAs("ines");
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/entities/Request/records/{id:D}", Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<Guid> StartAndRunAsync(string tenant, string process)
    {
        var subject = await fixture.InsertRequestAsync(tenant, null);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return instance;
    }

    private async Task<JsonDocument> GetOkAsync(string tenant, string process, Guid instance, string user = "ines")
    {
        using var response = await GetAsync(user, tenant, process, instance.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task<JsonDocument> GetOkAsync(WebApplicationFactory<Program> factory, string user, string tenant, string process, Guid instance)
    {
        using var client = factory.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/{process}/instances/{instance:D}", Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string tenant, string process, string id)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/{process}/instances/{id}", Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }
}
