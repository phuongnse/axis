using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskCommandTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string AllTasks = Tasks + "?pageSize=100";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_task_past_its_due_date_is_overdue_in_the_list_and_the_read_and_one_not_yet_due_is_not()
    {
        var (late, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        var (early, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        await SetDueAsync(late, "now() - interval '1 hour'");
        await SetDueAsync(early, "now() + interval '1 hour'");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");

        using var list = await ListAsync(fiona, TenantA);
        Assert.True(Item(list, late).GetProperty("overdue").GetBoolean());
        Assert.False(Item(list, early).GetProperty("overdue").GetBoolean());

        using var lateRead = await ReadAsync(fiona, late);
        Assert.True(lateRead.RootElement.GetProperty("overdue").GetBoolean());
        using var earlyRead = await ReadAsync(fiona, early);
        Assert.False(earlyRead.RootElement.GetProperty("overdue").GetBoolean());
    }

    [Fact]
    public async Task A_completed_task_past_its_due_date_is_not_overdue_and_has_no_commands()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        await SetDueAsync(task, "now() - interval '1 hour'");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");

        using var completion = await PostAsync(fiona, task, "complete", """{ "outcome": "approve" }""");

        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        using var completed = await ReadJsonAsync(completion);
        AssertSettled(completed);
        using var read = await ReadAsync(fiona, task);
        AssertSettled(read);
        Assert.Equal("completed", read.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Commands_follow_who_holds_the_role_who_claimed_and_who_holds_the_reassign_grant()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var lena = fixture.CreateClient().SignedInAs("lena");
        using var ines = fixture.CreateClient().SignedInAs("ines");

        using var unclaimed = await ReadAsync(fiona, task);
        Assert.Equal(["claim"], Commands(unclaimed));
        Assert.Empty(unclaimed.RootElement.GetProperty("reassignRoles").EnumerateArray());

        using var claim = await PostAsync(maria, task, "claim");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        using var claimed = await ReadJsonAsync(claim);
        Assert.Equal(["release"], Commands(claimed));
        using var mariaRead = await ReadAsync(maria, task);
        Assert.Equal(["release"], Commands(mariaRead));
        Assert.Empty(mariaRead.RootElement.GetProperty("reassignRoles").EnumerateArray());

        using var fionaRead = await ReadAsync(fiona, task);
        Assert.Empty(Commands(fionaRead));

        using var lenaRead = await ReadAsync(lena, task);
        Assert.Equal(["release", "reassign"], Commands(lenaRead));
        var role = Assert.Single(lenaRead.RootElement.GetProperty("reassignRoles").EnumerateArray(), candidate => candidate.GetProperty("name").GetString() == "finance");
        Assert.Equal("role.finance", role.GetProperty("labelKey").GetString());

        using var release = await PostAsync(ines, task, "release");
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        using var released = await ReadJsonAsync(release);
        Assert.Equal(["reassign"], Commands(released));
    }

    private static void AssertSettled(JsonDocument task)
    {
        Assert.False(task.RootElement.GetProperty("overdue").GetBoolean());
        Assert.Empty(Commands(task));
        Assert.Empty(task.RootElement.GetProperty("reassignRoles").EnumerateArray());
    }

    private static List<string> Commands(JsonDocument task) =>
        [.. task.RootElement.GetProperty("commands").EnumerateArray().Select(command => command.GetString()!)];

    private Task<int> SetDueAsync(Guid task, string due) =>
        fixture.ExecuteAsync(TenantA, $"UPDATE axis.process_tasks SET due_at = {due} WHERE id = @id", ("id", task));

    /// <summary>
    /// Creates a request in a department managed by <c>maria</c>, starts <paramref name="process"/>
    /// for it and runs the tenant's work items, so the instance waits on its task. Returns the
    /// task's id, the request's id and the instance's id.
    /// </summary>
    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string tenant, string process)
    {
        var department = await fixture.InsertDepartmentAsync(tenant, "maria");
        var subject = await fixture.InsertRequestAsync(tenant, department);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return (await fixture.TaskIdAsync(tenant, instance), subject, instance);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid task, string action, string? body = null)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/{action}", Host(TenantA), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, Guid task)
    {
        using var request = RecordApiFixture.Request($"{Tasks}/{task:D}", Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ListAsync(HttpClient client, string tenant)
    {
        using var request = RecordApiFixture.Request(AllTasks, Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    private static JsonElement Item(JsonDocument list, Guid id) =>
        list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
}
