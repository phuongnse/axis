using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
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
            ["id", "process", "subjectId", "releaseId", "state", "step", "createdAt", "endedAt", "parent", "subProcesses", "graph", "path", "tasks", "calls", "commands"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(instance, root.GetProperty("id").GetGuid());
        Assert.Equal("Break", root.GetProperty("process").GetString());
        Assert.Equal("failed", root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("parent").ValueKind);
        Assert.Empty(root.GetProperty("subProcesses").EnumerateArray());
        Assert.Equal(DateTimeKind.Utc, root.GetProperty("createdAt").GetDateTime().Kind);
        var path = Assert.Single(root.GetProperty("path").EnumerateArray());
        Assert.Equal(
            ["step", "startedAt", "finishedAt", "decision", "output", "error"],
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

        using var body = await GetOkAsync(TenantA, "RoleApprove", instance);

        var root = body.RootElement;
        Assert.Equal("waiting", root.GetProperty("state").GetString());
        Assert.Equal("approve", root.GetProperty("step").GetString());
        var task = Assert.Single(root.GetProperty("tasks").EnumerateArray());
        Assert.Equal(
            ["id", "step", "assigneeKind", "assignee", "state", "outcome", "completedBy", "createdAt", "completedAt"],
            task.EnumerateObject().Select(property => property.Name));
        Assert.Equal(taskId, task.GetProperty("id").GetGuid());
        Assert.Equal("approve", task.GetProperty("step").GetString());
        Assert.Equal("role", task.GetProperty("assigneeKind").GetString());
        Assert.Equal("finance", task.GetProperty("assignee").GetString());
        Assert.Equal("open", task.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);

        var path = Assert.Single(root.GetProperty("path").EnumerateArray());
        Assert.Equal("approve", path.GetProperty("step").GetString());
        Assert.Equal("waiting", path.GetProperty("output").GetProperty("state").GetString());
        Assert.Equal(taskId, path.GetProperty("output").GetProperty("taskId").GetGuid());

        var graph = root.GetProperty("graph");
        Assert.Equal("approve", graph.GetProperty("start").GetString());
        var steps = graph.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["approve", "done"], steps.Select(step => step.GetProperty("name").GetString()));
        Assert.Equal(["name", "type", "labelKey", "transitions"], steps[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal("task", steps[0].GetProperty("type").GetString());
        Assert.Equal("request.approve", steps[0].GetProperty("labelKey").GetString());
        Assert.Equal([("done", "approve")], Transitions(steps[0]));
        Assert.Equal("end", steps[1].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, steps[1].GetProperty("labelKey").ValueKind);
        Assert.Empty(steps[1].GetProperty("transitions").EnumerateArray());
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
        Assert.Equal("end", steps[1].GetProperty("type").GetString());
        Assert.Empty(steps[1].GetProperty("transitions").EnumerateArray());
        Assert.Empty(root.GetProperty("path").EnumerateArray());
        Assert.Empty(root.GetProperty("tasks").EnumerateArray());
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
            ["kind", "number", "startedAt", "finishedAt", "outcome", "httpStatus", "error"],
            attempts[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, attempts[0].GetProperty("error").ValueKind);
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
    public async Task A_sensitive_field_is_in_the_record_but_the_input_that_maps_it_is_hidden_from_a_user_who_may_read_it()
    {
        await fixture.ActivateEditedAsync(TenantB, MarkSensitive("amount"));
        var subject = await fixture.InsertRequestAsync(TenantB, null, amount: 500m);
        var instance = await fixture.StartAsync(TenantB, "DepartmentOrder", subject);
        await fixture.InsertConnectorCallAsync(TenantB, instance, "createOrder", """{"reference":"Sales","amount":500}""");

        using var client = fixture.CreateClient().SignedInAs("ada");
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/entities/Request/records/{subject:D}", Host(TenantB));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var record = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(500, record.RootElement.GetProperty("values").GetProperty("amount").GetDecimal());

        using var body = await GetOkAsync(TenantB, "DepartmentOrder", instance, "ada");
        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
        Assert.False(call.TryGetProperty("amount", out _));
    }

    [Fact]
    public async Task An_instance_pinned_to_a_release_that_marks_a_field_sensitive_keeps_its_input_hidden_after_a_newer_release_drops_the_flag()
    {
        var flagged = await fixture.ActivateEditedAsync(TenantB, MarkSensitive("amount"));
        var subject = await fixture.InsertRequestAsync(TenantB, null, amount: 500m);
        var instance = await fixture.StartAsync(TenantB, "DepartmentOrder", subject, flagged);
        await fixture.InsertConnectorCallAsync(TenantB, instance, "createOrder", """{"reference":"Sales","amount":500}""");
        await fixture.ActivateEditedAsync(TenantB, MarkSensitive("divisor"));

        using var body = await GetOkAsync(TenantB, "DepartmentOrder", instance, "ada");

        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
    }

    [Fact]
    public async Task Adding_the_sensitive_flag_in_a_new_release_changes_no_table()
    {
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        var before = await fixture.ReadTableAsync(TenantB, request.Id);

        await fixture.ActivateEditedAsync(TenantB, MarkSensitive("amount", "department"));

        var after = await fixture.ReadTableAsync(TenantB, request.Id);
        Assert.Equal(before.Columns, after.Columns);
    }

    // Edits the Request entity so that each of the named fields declares "sensitive": true. A notify step may not
    // read a sensitive field, so the notify processes and the notify escalation of the app give the same values a literal instead.
    private static Func<string, string, string?> MarkSensitive(params string[] fields) =>
        (path, content) =>
        {
            if (path == Path.Combine("processes", "notify.json")
                || path == Path.Combine("processes", "notify-email.json")
                || path == Path.Combine("processes", "escalate-notify.json"))
            {
                return fields.Aggregate(content, (text, field) => text.Replace($"\"{field}\": \"{field}\"", $"\"{field}\": \"0\"", StringComparison.Ordinal));
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
            "UPDATE axis.process_instances SET parent_instance_id = @parent, parent_step = @step WHERE id = @id",
            ("parent", parent),
            ("step", step),
            ("id", child));
        return child;
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

    private async Task<HttpResponseMessage> GetAsync(string user, string tenant, string process, string id)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/{process}/instances/{id}", Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }
}
