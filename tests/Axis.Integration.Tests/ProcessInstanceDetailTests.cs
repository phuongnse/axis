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
            ["id", "process", "subjectId", "releaseId", "state", "step", "createdAt", "endedAt", "graph", "path", "tasks"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(instance, root.GetProperty("id").GetGuid());
        Assert.Equal("Break", root.GetProperty("process").GetString());
        Assert.Equal("failed", root.GetProperty("state").GetString());
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

    private static List<(string? Next, string? On)> Transitions(JsonElement step) =>
        step.GetProperty("transitions").EnumerateArray()
            .Select(transition => (transition.GetProperty("next").GetString(), transition.GetProperty("on").GetString()))
            .ToList();

    private async Task<Guid> StartAndRunAsync(string tenant, string process)
    {
        var subject = await fixture.InsertRequestAsync(tenant, null);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return instance;
    }

    private async Task<JsonDocument> GetOkAsync(string tenant, string process, Guid instance)
    {
        using var response = await GetAsync("ines", tenant, process, instance.ToString("D"));
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
