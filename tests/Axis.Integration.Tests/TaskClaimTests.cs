using System.Net;
using System.Text.Json;
using Axis.Data.Audit;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskClaimTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string AllTasks = Tasks + "?pageSize=100";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_claimed_role_task_leaves_the_other_holders_list_and_only_the_claimer_completes_it()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");

        using var claim = await PostAsync(maria, task, "claim", TenantA);

        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        using var claimed = await ReadJsonAsync(claim);
        Assert.Equal(task, claimed.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("open", claimed.RootElement.GetProperty("state").GetString());
        Assert.Equal("maria", claimed.RootElement.GetProperty("claimedBy").GetString());
        Assert.Equal("finance", claimed.RootElement.GetProperty("assignee").GetProperty("role").GetString());

        using var fionaList = await ListAsync(fiona, TenantA);
        Assert.DoesNotContain(task, Ids(fionaList));
        using var mariaList = await ListAsync(maria, TenantA);
        Assert.Equal("maria", Item(mariaList, task).GetProperty("claimedBy").GetString());

        using var fionaRead = await GetAsync(fiona, task, TenantA);
        Assert.Equal(HttpStatusCode.OK, fionaRead.StatusCode);
        using var read = await ReadJsonAsync(fionaRead);
        Assert.Equal("maria", read.RootElement.GetProperty("claimedBy").GetString());

        using var refused = await PostAsync(fiona, task, "complete", TenantA, """{ "outcome": "approve" }""");
        using var problem = await RecordApiFixture.ReadProblemAsync(refused, HttpStatusCode.Forbidden);
        Assert.Equal("This task is assigned to someone else.", problem.RootElement.GetProperty("title").GetString());

        using var completed = await PostAsync(maria, task, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task Claiming_a_user_task_is_409()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "Approve");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await PostAsync(maria, task, "claim", TenantA);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("Only a task assigned to a role can be claimed.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Claiming_a_task_another_holder_claimed_is_409_and_so_is_claiming_it_again()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var first = await PostAsync(maria, task, "claim", TenantA);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        foreach (var client in new[] { fiona, maria })
        {
            using var response = await PostAsync(client, task, "claim", TenantA);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
            Assert.Equal("This task is already claimed.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task A_user_without_the_role_cannot_claim_and_an_unknown_task_is_404()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var otto = fixture.CreateClient().SignedInAs("otto");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var denied = await PostAsync(otto, task, "claim", TenantA);
        using var deniedProblem = await RecordApiFixture.ReadProblemAsync(denied, HttpStatusCode.Forbidden);
        Assert.Equal("This task is assigned to someone else.", deniedProblem.RootElement.GetProperty("title").GetString());

        using var unknown = await PostAsync(maria, Guid.Parse("0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01"), "claim", TenantA);
        using var unknownProblem = await RecordApiFixture.ReadProblemAsync(unknown, HttpStatusCode.NotFound);
        Assert.Equal("No task exists with this id.", unknownProblem.RootElement.GetProperty("title").GetString());

        using var unclaimed = await ListAsync(maria, TenantA);
        Assert.Equal(JsonValueKind.Null, Item(unclaimed, task).GetProperty("claimedBy").ValueKind);
    }

    [Fact]
    public async Task Only_the_claimer_releases_the_task_and_an_unclaimed_task_cannot_be_released()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var otto = fixture.CreateClient().SignedInAs("otto");

        using var unclaimed = await PostAsync(maria, task, "release", TenantA);
        using var unclaimedProblem = await RecordApiFixture.ReadProblemAsync(unclaimed, HttpStatusCode.Conflict);
        Assert.Equal("This task is not claimed.", unclaimedProblem.RootElement.GetProperty("title").GetString());

        using var claim = await PostAsync(maria, task, "claim", TenantA);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        using var other = await PostAsync(fiona, task, "release", TenantA);
        using var otherProblem = await RecordApiFixture.ReadProblemAsync(other, HttpStatusCode.Forbidden);
        Assert.Equal("This task is assigned to someone else.", otherProblem.RootElement.GetProperty("title").GetString());

        using var outsider = await PostAsync(otto, task, "release", TenantA);
        await RecordApiFixture.ReadProblemAsync(outsider, HttpStatusCode.Forbidden);

        using var mariaList = await ListAsync(maria, TenantA);
        Assert.Equal("maria", Item(mariaList, task).GetProperty("claimedBy").GetString());
    }

    [Fact]
    public async Task The_claimer_releases_the_task_and_the_other_holder_lists_it_again()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var claim = await PostAsync(maria, task, "claim", TenantA);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        using var release = await PostAsync(maria, task, "release", TenantA);

        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        using var released = await ReadJsonAsync(release);
        Assert.Equal(JsonValueKind.Null, released.RootElement.GetProperty("claimedBy").ValueKind);
        using var fionaList = await ListAsync(fiona, TenantA);
        Assert.Equal(JsonValueKind.Null, Item(fionaList, task).GetProperty("claimedBy").ValueKind);

        var (claimedActor, claimedName, claimedDetails) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskClaimed));
        Assert.Equal(("maria", "Maria Manager"), (claimedActor, claimedName));
        Assert.Equal(task, JsonDocument.Parse(claimedDetails).RootElement.GetProperty("taskId").GetGuid());
        var (releasedActor, releasedName, _) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReleased));
        Assert.Equal(("maria", "Maria Manager"), (releasedActor, releasedName));

        using var completed = await PostAsync(fiona, task, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task A_grant_holder_without_the_role_releases_another_users_claim()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var ines = fixture.CreateClient().SignedInAs("ines");
        using var claim = await PostAsync(maria, task, "claim", TenantA);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        using var release = await PostAsync(ines, task, "release", TenantA);

        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        using var released = await ReadJsonAsync(release);
        Assert.Equal(JsonValueKind.Null, released.RootElement.GetProperty("claimedBy").ValueKind);
        var (actor, actorName, _) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReleased));
        Assert.Equal(("ines", "Ines Inspector"), (actor, actorName));
        using var completed = await PostAsync(fiona, task, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task Both_routes_need_a_signed_in_user()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var client = fixture.CreateClient();

        foreach (var action in new[] { "claim", "release" })
        {
            using var response = await PostAsync(client, task, action, TenantA);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        }
    }

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

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid task, string action, string tenant, string? body = null)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/{action}", Host(tenant), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Guid task, string tenant)
    {
        using var request = RecordApiFixture.Request($"{Tasks}/{task:D}", Host(tenant));
        return await client.SendAsync(request, CancellationToken);
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

    private static List<Guid> Ids(JsonDocument list) =>
        [.. list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

    private static JsonElement Item(JsonDocument list, Guid id) =>
        list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
}
