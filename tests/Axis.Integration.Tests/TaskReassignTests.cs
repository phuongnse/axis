using System.Net;
using System.Text.Json;
using Axis.Data.Audit;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskReassignTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string Users = "/api/apps/StepApp/users";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grant_holder_without_the_role_hands_a_claimed_task_to_a_known_user_who_alone_completes_it()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "RoleApprove");
        await fixture.InsertProfileAsync(TenantA, "carl", "Carl Clerk");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var carl = fixture.CreateClient().SignedInAs("carl");
        using var ines = fixture.CreateClient().SignedInAs("ines");
        using var claim = await PostAsync(maria, task, "claim", TenantA);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        using var reassign = await PostAsync(ines, task, "reassign", TenantA, """{ "user": "carl", "reason": "On leave." }""");

        Assert.Equal(HttpStatusCode.OK, reassign.StatusCode);
        using var reassigned = await ReadJsonAsync(reassign);
        Assert.Equal(task, reassigned.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("open", reassigned.RootElement.GetProperty("state").GetString());
        Assert.Equal("carl", reassigned.RootElement.GetProperty("assignee").GetProperty("user").GetString());
        Assert.Equal(JsonValueKind.Null, reassigned.RootElement.GetProperty("assignee").GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.Null, reassigned.RootElement.GetProperty("claimedBy").ValueKind);

        foreach (var holder in new[] { maria, fiona })
        {
            using var refused = await PostAsync(holder, task, "complete", TenantA, """{ "outcome": "approve" }""");
            await RecordApiFixture.ReadProblemAsync(refused, HttpStatusCode.Forbidden);
        }

        using var completed = await PostAsync(carl, task, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        var (actor, actorName, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        Assert.Equal(("ines", "Ines Inspector"), (actor, actorName));
        using var audit = JsonDocument.Parse(details);
        Assert.Equal(task, audit.RootElement.GetProperty("taskId").GetGuid());
        Assert.Equal("finance", audit.RootElement.GetProperty("from").GetProperty("role").GetString());
        Assert.Equal("carl", audit.RootElement.GetProperty("to").GetProperty("user").GetString());
        Assert.Equal("On leave.", audit.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_grant_holder_hands_a_task_to_a_declared_role()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Approve");
        using var ines = fixture.CreateClient().SignedInAs("ines");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var carl = fixture.CreateClient().SignedInAs("carl");

        // ines holds reassign on RoleApprove only, so the grant is per process.
        using var denied = await PostAsync(ines, task, "reassign", TenantA, """{ "role": "clerk", "reason": "Wrong desk." }""");
        await RecordApiFixture.ReadProblemAsync(denied, HttpStatusCode.Forbidden);

        var (roleTask, _, roleInstance) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var response = await PostAsync(ines, roleTask, "reassign", TenantA, """{ "role": "clerk", "reason": "Wrong desk." }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reassigned = await ReadJsonAsync(response);
        Assert.Equal("clerk", reassigned.RootElement.GetProperty("assignee").GetProperty("role").GetString());
        using var mariaRefused = await PostAsync(maria, roleTask, "complete", TenantA, """{ "outcome": "approve" }""");
        await RecordApiFixture.ReadProblemAsync(mariaRefused, HttpStatusCode.Forbidden);
        using var completed = await PostAsync(carl, roleTask, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var (_, _, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, roleInstance, AuditActions.TaskReassigned));
        Assert.Equal("clerk", JsonDocument.Parse(details).RootElement.GetProperty("to").GetProperty("role").GetString());
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
    }

    [Fact]
    public async Task The_assignee_without_the_grant_is_refused_and_nothing_changes()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "RoleApprove");
        await fixture.InsertProfileAsync(TenantA, "carl", "Carl Clerk");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var otto = fixture.CreateClient().SignedInAs("otto");

        foreach (var client in new[] { maria, otto })
        {
            using var response = await PostAsync(client, task, "reassign", TenantA, """{ "user": "carl", "reason": "Not mine." }""");
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
        using var completed = await PostAsync(maria, task, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task An_undeclared_role_and_a_user_with_no_profile_are_400_at_their_own_pointers()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var ines = fixture.CreateClient().SignedInAs("ines");

        using var role = await PostAsync(ines, task, "reassign", TenantA, """{ "role": "nobody", "reason": "Why not." }""");
        using var roleProblem = await RecordApiFixture.ReadProblemAsync(role, HttpStatusCode.BadRequest);
        var roleErrors = roleProblem.RootElement.GetProperty("errors");
        Assert.Equal(["/role"], roleErrors.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["No role with this name is declared."], roleErrors.GetProperty("/role").EnumerateArray().Select(message => message.GetString()));

        using var user = await PostAsync(ines, task, "reassign", TenantA, $$"""{ "user": "ghost-{{Guid.NewGuid():N}}", "reason": "Why not." }""");
        using var userProblem = await RecordApiFixture.ReadProblemAsync(user, HttpStatusCode.BadRequest);
        var userErrors = userProblem.RootElement.GetProperty("errors");
        Assert.Equal(["/user"], userErrors.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["No user with this id has signed in."], userErrors.GetProperty("/user").EnumerateArray().Select(message => message.GetString()));

        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskReassigned));
    }

    [Theory]
    [InlineData("""{ "reason": "Why." }""", "")]
    [InlineData("""{ "user": "carl", "role": "clerk", "reason": "Why." }""", "")]
    [InlineData("""{ "role": "clerk" }""", "/reason")]
    [InlineData("""{ "role": "clerk", "reason": "   " }""", "/reason")]
    [InlineData("""{ "role": "", "reason": "Why." }""", "/role")]
    [InlineData("""{ "user": 7, "reason": "Why." }""", "/user")]
    [InlineData("""{ "role": "clerk", "reason": "Why.", "extra": 1 }""", "/extra")]
    [InlineData("""[]""", "")]
    public async Task A_malformed_body_is_400_at_its_pointer(string body, string key)
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var ines = fixture.CreateClient().SignedInAs("ines");

        using var response = await PostAsync(ines, task, "reassign", TenantA, body);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([key], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_400()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var ines = fixture.CreateClient().SignedInAs("ines");

        using var response = await PostAsync(ines, task, "reassign", TenantA, $$"""{ "role": "clerk", "reason": "{{new string('x', 501)}}" }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be at most 500 characters."], errors.GetProperty("/reason").EnumerateArray().Select(message => message.GetString()));
    }

    [Fact]
    public async Task The_route_answers_401_404_409_and_415_in_the_task_api_order()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var ines = fixture.CreateClient().SignedInAs("ines");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        const string body = """{ "role": "clerk", "reason": "Why." }""";

        using var anonymous = await PostAsync(fixture.CreateClient(), task, "reassign", TenantA, body);
        await RecordApiFixture.ReadProblemAsync(anonymous, HttpStatusCode.Unauthorized);

        using var unknown = await PostAsync(ines, Guid.Parse("0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01"), "reassign", TenantA, body);
        await RecordApiFixture.ReadProblemAsync(unknown, HttpStatusCode.NotFound);

        using var text = await PostAsync(ines, task, "reassign", TenantA, body, "text/plain");
        await RecordApiFixture.ReadProblemAsync(text, HttpStatusCode.UnsupportedMediaType);

        using var completed = await PostAsync(maria, task, "complete", TenantA, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var closed = await PostAsync(ines, task, "reassign", TenantA, body);
        using var problem = await RecordApiFixture.ReadProblemAsync(closed, HttpStatusCode.Conflict);
        Assert.Equal("This task is already completed.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_picker_lists_the_profiles_that_match_mixed_case_text_for_a_grant_holder_and_is_403_for_anyone_else()
    {
        var name = $"Zelda {Guid.NewGuid():N}";
        await fixture.InsertProfileAsync(TenantA, "zelda-picker", name);
        await fixture.InsertProfileAsync(TenantA, "zelda-other", "Somebody Else " + Guid.NewGuid().ToString("N"));
        using var ines = fixture.CreateClient().SignedInAs("ines");

        using var response = await GetUsersAsync(ines, TenantA, "?search=" + Uri.EscapeDataString(name[3..].ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var list = await ReadJsonAsync(response);
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(["displayName", "id"], item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("zelda-picker", name), (item.GetProperty("id").GetString(), item.GetProperty("displayName").GetString()));

        foreach (var user in new[] { "otto", "maria", "ada" })
        {
            using var client = fixture.CreateClient().SignedInAs(user);
            using var denied = await GetUsersAsync(client, TenantA, "?search=zelda");
            using var problem = await RecordApiFixture.ReadProblemAsync(denied, HttpStatusCode.Forbidden);
            Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        }

        using var anonymous = await GetUsersAsync(fixture.CreateClient(), TenantA, "?search=zelda");
        using var anonymousProblem = await RecordApiFixture.ReadProblemAsync(anonymous, HttpStatusCode.Unauthorized);
        Assert.Equal("Sign in to read or write this data.", anonymousProblem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_picker_lists_at_most_20_profiles_in_order_and_treats_percent_and_underscore_as_text()
    {
        var prefix = $"Pick{Guid.NewGuid():N}";
        for (var index = 0; index < 22; index++)
        {
            await fixture.InsertProfileAsync(TenantA, $"{prefix}-{index:D2}", $"{prefix} {index:D2}");
        }

        using var ines = fixture.CreateClient().SignedInAs("ines");

        using var listed = await GetUsersAsync(ines, TenantA, "?search=" + prefix);
        using var list = await ReadJsonAsync(listed);
        var names = list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("displayName").GetString()).ToList();
        Assert.Equal(Enumerable.Range(0, 20).Select(index => $"{prefix} {index:D2}"), names);

        using var wildcard = await GetUsersAsync(ines, TenantA, "?search=" + Uri.EscapeDataString(prefix[..4] + "%"));
        using var wildcards = await ReadJsonAsync(wildcard);
        Assert.Empty(wildcards.RootElement.GetProperty("items").EnumerateArray());

        using var empty = await GetUsersAsync(ines, TenantA, "");
        using var emptyList = await ReadJsonAsync(empty);
        Assert.Equal(20, emptyList.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// Starts <paramref name="process"/> for a new request and runs the tenant's work items, so the
    /// instance waits on its task. Returns the task's id, the request's id and the instance's id.
    /// </summary>
    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string tenant, string process)
    {
        var department = await fixture.InsertDepartmentAsync(tenant, "maria");
        var subject = await fixture.InsertRequestAsync(tenant, department);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return (await fixture.TaskIdAsync(tenant, instance), subject, instance);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, Guid task, string action, string tenant, string? body = null, string? contentType = "application/json")
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/{action}", Host(tenant), body, contentType);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetUsersAsync(HttpClient client, string tenant, string query)
    {
        using var request = RecordApiFixture.Request(Users + query, Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
