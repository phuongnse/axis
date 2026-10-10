using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskEndpointTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string AllTasks = Tasks + "?pageSize=100";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_manager_lists_their_task_and_every_role_holder_lists_the_role_task_with_undated_tasks_last()
    {
        var (userTask, userSubject) = await CreateTaskAsync(TenantA, "Approve");
        var (roleTask, roleSubject) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var maria = await SignInAsync("maria", TenantA);
        using var fiona = await SignInAsync("fiona", TenantA);

        using var mariaList = await ListAsync(maria, TenantA);
        var mariaIds = Ids(mariaList);
        Assert.Contains(userTask, mariaIds);
        Assert.Contains(roleTask, mariaIds);
        Assert.True(mariaIds.IndexOf(userTask) < mariaIds.IndexOf(roleTask), "A task with a due date comes before a task without one.");

        using var fionaList = await ListAsync(fiona, TenantA);
        var fionaIds = Ids(fionaList);
        Assert.Contains(roleTask, fionaIds);
        Assert.DoesNotContain(userTask, fionaIds);

        var root = mariaList.RootElement;
        Assert.Equal(["items", "page", "pageSize", "totalCount"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(100, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(mariaIds.Count, root.GetProperty("totalCount").GetInt64());

        var item = Item(mariaList, userTask);
        Assert.Equal(
            ["id", "process", "step", "labelKey", "subject", "dueAt", "createdAt"],
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Approve", item.GetProperty("process").GetString());
        Assert.Equal("approve", item.GetProperty("step").GetString());
        Assert.Equal("request.approve", item.GetProperty("labelKey").GetString());
        AssertSubject(item.GetProperty("subject"), userSubject);
        Assert.EndsWith("Z", item.GetProperty("dueAt").GetString(), StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromDays(3), item.GetProperty("dueAt").GetDateTimeOffset() - item.GetProperty("createdAt").GetDateTimeOffset());

        var roleItem = Item(fionaList, roleTask);
        Assert.Equal("RoleApprove", roleItem.GetProperty("process").GetString());
        Assert.Equal(JsonValueKind.Null, roleItem.GetProperty("dueAt").ValueKind);
        AssertSubject(roleItem.GetProperty("subject"), roleSubject);
    }

    [Fact]
    public async Task A_user_who_may_not_act_neither_lists_nor_reads_the_tasks()
    {
        var (userTask, _) = await CreateTaskAsync(TenantA, "Approve");
        var (roleTask, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var otto = await SignInAsync("otto", TenantA);

        using var list = await ListAsync(otto, TenantA);
        var ids = Ids(list);
        Assert.DoesNotContain(userTask, ids);
        Assert.DoesNotContain(roleTask, ids);

        foreach (var task in new[] { userTask, roleTask })
        {
            using var response = await SendAsync(otto, $"{Tasks}/{task:D}", TenantA);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("This task is assigned to someone else.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task The_manager_reads_their_task_with_its_form_from_the_tasks_release()
    {
        var (task, subject) = await CreateTaskAsync(TenantA, "Approve");
        using var maria = await SignInAsync("maria", TenantA);

        using var response = await SendAsync(maria, $"{Tasks}/{task:D}", TenantA);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.Equal(
            [
                "id", "process", "instanceId", "step", "labelKey", "state", "assignee", "subject", "form", "outcomes",
                "dueAt", "createdAt", "completedAt", "completedBy", "outcome",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(task, root.GetProperty("id").GetGuid());
        Assert.Equal("Approve", root.GetProperty("process").GetString());
        Assert.Equal("approve", root.GetProperty("step").GetString());
        Assert.Equal("request.approve", root.GetProperty("labelKey").GetString());
        Assert.Equal("open", root.GetProperty("state").GetString());
        Assert.Equal("maria", root.GetProperty("assignee").GetProperty("user").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assignee").GetProperty("role").ValueKind);
        AssertSubject(root.GetProperty("subject"), subject);
        Assert.Equal(
            [("approve", "task.approve")],
            root.GetProperty("outcomes").EnumerateArray().Select(outcome =>
                (outcome.GetProperty("name").GetString(), outcome.GetProperty("labelKey").GetString())));

        var form = root.GetProperty("form");
        Assert.Equal("RequestReview", form.GetProperty("name").GetString());
        var section = Assert.Single(form.GetProperty("sections").EnumerateArray());
        Assert.Equal("requestReview.main", section.GetProperty("titleKey").GetString());
        var field = Assert.Single(section.GetProperty("fields").EnumerateArray());
        Assert.Equal("title", field.GetProperty("name").GetString());
        Assert.False(field.GetProperty("readOnly").GetBoolean());
        var entity = form.GetProperty("entity");
        Assert.Equal("Request", entity.GetProperty("name").GetString());
        Assert.Contains("title", entity.GetProperty("fields").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()));

        Assert.EndsWith("Z", root.GetProperty("createdAt").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
    }

    [Fact]
    public async Task A_role_holder_reads_the_role_task()
    {
        var (task, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var fiona = await SignInAsync("fiona", TenantA);

        using var response = await SendAsync(fiona, $"{Tasks}/{task:D}", TenantA);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var assignee = body.RootElement.GetProperty("assignee");
        Assert.Equal(JsonValueKind.Null, assignee.GetProperty("user").ValueKind);
        Assert.Equal("finance", assignee.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Both_routes_need_a_signed_in_user()
    {
        var (task, _) = await CreateTaskAsync(TenantA, "Approve");
        using var client = fixture.CreateClient();

        foreach (var path in new[] { Tasks, $"{Tasks}/{task:D}", "/api/apps/NoSuchApp/tasks", $"{Tasks}/not-a-uuid" })
        {
            using var response = await SendAsync(client, path, TenantA);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to work on tasks.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Theory]
    [InlineData("/api/apps/NoSuchApp/tasks", "No application is active under this name.")]
    [InlineData("/api/apps/NoSuchApp/tasks/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01", "No application is active under this name.")]
    [InlineData(Tasks + "/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01", "No task exists with this id.")]
    [InlineData(Tasks + "/not-a-uuid", "No task exists with this id.")]
    [InlineData(Tasks + "/0192f4b17a2c7d3e8f405b6c7d8e9f01", "No task exists with this id.")]
    [InlineData(Tasks + "/{0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01}", "No task exists with this id.")]
    public async Task An_unknown_application_or_task_is_404(string path, string title)
    {
        using var maria = await SignInAsync("maria", TenantA);

        using var response = await SendAsync(maria, path, TenantA);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?page=0", "page")]
    [InlineData("?page=x", "page")]
    public async Task An_invalid_page_is_400(string query, string key)
    {
        using var maria = await SignInAsync("maria", TenantA);

        using var response = await SendAsync(maria, Tasks + query, TenantA);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([key], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task A_task_of_one_tenant_is_neither_listed_nor_read_on_another_tenants_host()
    {
        var (taskA, _) = await CreateTaskAsync(TenantA, "Approve");
        var (taskB, _) = await CreateTaskAsync(TenantB, "Approve");
        using var maria = await SignInAsync("maria", TenantB);

        using var list = await ListAsync(maria, TenantB);
        var ids = Ids(list);
        Assert.Contains(taskB, ids);
        Assert.DoesNotContain(taskA, ids);

        using var read = await SendAsync(maria, $"{Tasks}/{taskA:D}", TenantB);
        using var problem = await RecordApiFixture.ReadProblemAsync(read, HttpStatusCode.NotFound);
        Assert.Equal("No task exists with this id.", problem.RootElement.GetProperty("title").GetString());

        using var listA = await ListAsync(maria, TenantA);
        var idsA = Ids(listA);
        Assert.Contains(taskA, idsA);
        Assert.DoesNotContain(taskB, idsA);
    }

    /// <summary>
    /// Creates a request in a department managed by <c>maria</c>, starts <paramref name="process"/>
    /// for it and runs the tenant's work items, so the instance waits on its task. Returns the
    /// task's id and the request's id.
    /// </summary>
    private async Task<(Guid Task, Guid Subject)> CreateTaskAsync(string tenant, string process)
    {
        var department = await fixture.InsertDepartmentAsync(tenant, "maria");
        var subject = await fixture.InsertRequestAsync(tenant, department);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return (await fixture.TaskIdAsync(tenant, instance), subject);
    }

    private async Task<HttpClient> SignInAsync(string user, string tenant)
    {
        var client = fixture.CreateClient();
        using var signIn = RecordApiFixture.Request(HttpMethod.Post, "/api/test-users/sign-in", Host(tenant), $$"""{ "id": "{{user}}" }""");
        using var response = await client.SendAsync(signIn, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string tenant)
    {
        using var request = RecordApiFixture.Request(path, Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ListAsync(HttpClient client, string tenant)
    {
        using var response = await SendAsync(client, AllTasks, tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    private static List<Guid> Ids(JsonDocument list) =>
        [.. list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

    private static JsonElement Item(JsonDocument list, Guid id) =>
        list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);

    private static void AssertSubject(JsonElement subject, Guid id)
    {
        Assert.Equal("Request", subject.GetProperty("entity").GetString());
        Assert.Equal(id, subject.GetProperty("id").GetGuid());
        Assert.Equal(RequestTitle(id), subject.GetProperty("label").GetString());
    }
}
