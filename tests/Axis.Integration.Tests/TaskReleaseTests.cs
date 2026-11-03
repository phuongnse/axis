using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A task pinned to release A, read after a release B is activated. The label comes from A's
/// display field and the texts served by its release id, the readable fields and the role assignee
/// check follow B.
/// </summary>
public sealed class TaskReleaseTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string AllTasks = Tasks + "?pageSize=100";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_task_keeps_the_display_field_of_its_release_after_a_release_changes_it()
    {
        // A display field must be a required text field, and a required field can be added only to
        // an empty table. So release B adds the required "code", and the request is replaced after
        // the task exists. Tenant b is used only here, so its request table holds this request alone.
        var (task, subject, _) = await CreateTaskAsync("Approve", TenantB);
        await fixture.DeleteRequestAsync(TenantB, subject);
        await fixture.ActivateEditedAsync(TenantB, (path, content) =>
            path == Path.Combine("entities", "request.json")
                ? content
                    .Replace("\"displayField\": \"title\"", "\"displayField\": \"code\"", StringComparison.Ordinal)
                    .Replace(
                        "\"fields\": [",
                        "\"fields\": [\n    { \"name\": \"code\", \"type\": \"text\", \"required\": true },",
                        StringComparison.Ordinal)
                : content);
        await fixture.InsertRequestWithCodeAsync(TenantB, subject, "CODE-1");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var list = await GetJsonAsync(maria, AllTasks, TenantB);
        var item = list.RootElement.GetProperty("items").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == task);
        Assert.Equal(RequestTitle(subject), item.GetProperty("subject").GetProperty("label").GetString());

        using var read = await GetJsonAsync(maria, $"{Tasks}/{task:D}", TenantB);
        Assert.Equal(RequestTitle(subject), read.RootElement.GetProperty("subject").GetProperty("label").GetString());
    }

    [Fact]
    public async Task A_task_started_on_release_A_returns_release_A_after_release_B_is_activated()
    {
        var (task, _, _) = await CreateTaskAsync("Approve");
        await ActivateRewordedAsync();
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var read = await GetJsonAsync(maria, $"{Tasks}/{task:D}");
        Assert.Equal(fixture.ReleaseId(TenantA), read.RootElement.GetProperty("releaseId").GetGuid());

        using var list = await GetJsonAsync(maria, AllTasks);
        var item = list.RootElement.GetProperty("items").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == task);
        Assert.Equal(fixture.ReleaseId(TenantA), item.GetProperty("releaseId").GetGuid());
    }

    [Fact]
    public async Task Completing_a_task_started_on_release_A_stamps_the_subject_with_release_A_while_release_B_is_active()
    {
        var (task, subject, instance) = await CreateTaskAsync("Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        var releaseB = await ActivateRewordedAsync();
        Assert.NotEqual(fixture.ReleaseId(TenantA), releaseB);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await CompleteAsync(
            maria, task, $$"""{ "outcome": "approve", "values": { "comment": "ok" }, "version": {{waiting.SubjectVersion}} }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fixture.ReleaseId(TenantA), await fixture.ComputedReleaseAsync(TenantA, subject));
    }

    [Fact]
    public async Task The_texts_of_release_A_are_served_by_its_id_while_release_B_is_active()
    {
        await ActivateRewordedAsync();
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var old = await GetJsonAsync(maria, $"/api/sites/steps/texts/en?release={fixture.ReleaseId(TenantA):D}");
        Assert.Equal("Approve the request", old.RootElement.GetProperty("texts").GetProperty("request.approve").GetString());

        using var active = await GetJsonAsync(maria, "/api/sites/steps/texts/en");
        Assert.Equal("Approve this request", active.RootElement.GetProperty("texts").GetProperty("request.approve").GetString());
    }

    [Theory]
    [InlineData("other")]
    [InlineData("unknown")]
    [InlineData("not-a-uuid")]
    public async Task The_texts_of_a_release_that_is_not_one_of_the_applications_are_a_404(string kind)
    {
        var release = kind switch
        {
            "other" => (await fixture.CompileFixtureAsync(TenantA, "SecondApp")).ToString("D"),
            "unknown" => Guid.NewGuid().ToString("D"),
            _ => kind,
        };
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await SendAsync(maria, $"/api/sites/steps/texts/en?release={release}");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal("No release of the site's application exists with this id.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_field_a_release_drops_from_the_read_grant_leaves_the_tasks_form_and_cannot_be_written()
    {
        var (task, _, instance) = await CreateTaskAsync("Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        await fixture.ActivateEditedAsync(TenantA, (path, content) =>
            path == Path.Combine("policies", "finance-access.json")
                ? content.Replace(
                    """{ "name": "requestRead", "entity": "Request", "actions": ["read", "create", "delete"] }""",
                    """{ "name": "requestRead", "entity": "Request", "actions": ["read", "create", "delete"], "fields": ["title", "amount", "divisor", "status", "submittedAt", "department"] }""",
                    StringComparison.Ordinal)
                : content);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var read = await GetJsonAsync(maria, $"{Tasks}/{task:D}");

        var form = read.RootElement.GetProperty("form");
        var section = Assert.Single(form.GetProperty("sections").EnumerateArray());
        Assert.Equal(["title", "amount"], section.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()));
        Assert.DoesNotContain(
            "comment",
            form.GetProperty("entity").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()));

        using var response = await CompleteAsync(
            maria, task, $$"""{ "outcome": "approve", "values": { "comment": "x" }, "version": {{waiting.SubjectVersion}} }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["/values/comment"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(["Cannot be set."], errors.GetProperty("/values/comment").EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(waiting, await fixture.SnapshotAsync(TenantA, instance));
    }

    [Fact]
    public async Task A_role_a_release_stops_declaring_no_longer_reaches_the_tasks_assigned_to_it()
    {
        var (roleTask, _, _) = await CreateTaskAsync("RoleApprove");
        var (userTask, _, _) = await CreateTaskAsync("Approve");
        await fixture.ActivateEditedAsync(TenantA, (path, content) =>
            path == Path.Combine("roles", "finance.json")
                ? null
                : content.Replace("\"finance\"", "\"reviewer\"", StringComparison.Ordinal));
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using (var list = await GetJsonAsync(fiona, AllTasks))
        {
            Assert.DoesNotContain(roleTask, Ids(list));
        }

        foreach (var client in new[] { fiona, rita })
        {
            using var read = await SendAsync(client, $"{Tasks}/{roleTask:D}");
            using var denied = await RecordApiFixture.ReadProblemAsync(read, HttpStatusCode.Forbidden);
            Assert.Equal("This task is assigned to someone else.", denied.RootElement.GetProperty("title").GetString());

            using var complete = await CompleteAsync(client, roleTask, """{ "outcome": "approve" }""");
            using var refused = await RecordApiFixture.ReadProblemAsync(complete, HttpStatusCode.Forbidden);
            Assert.Equal("This task is assigned to someone else.", refused.RootElement.GetProperty("title").GetString());
        }

        using (var list = await GetJsonAsync(maria, AllTasks))
        {
            Assert.Contains(userTask, Ids(list));
        }

        using var completed = await CompleteAsync(maria, userTask, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    // Release B rewords the Approve step's label, so the texts of A and B differ.
    private Task<Guid> ActivateRewordedAsync() =>
        fixture.ActivateEditedAsync(TenantA, (path, content) =>
            path == Path.Combine("texts", "en.json")
                ? content.Replace("Approve the request", "Approve this request", StringComparison.Ordinal)
                : content);

    /// <summary>
    /// Creates a request in a department managed by <c>maria</c>, starts <paramref name="process"/>
    /// for it on release A in <paramref name="tenant"/> and runs the tenant's work items, so the instance waits on its task.
    /// Returns the task's id, the request's id and the instance's id.
    /// </summary>
    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string process, string tenant = TenantA)
    {
        var department = await fixture.InsertDepartmentAsync(tenant, "maria");
        var subject = await fixture.InsertRequestAsync(tenant, department);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return (await fixture.TaskIdAsync(tenant, instance), subject, instance);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string tenant = TenantA)
    {
        using var request = RecordApiFixture.Request(path, Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid task, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(TenantA), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, string tenant = TenantA)
    {
        using var response = await SendAsync(client, path, tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> Ids(JsonDocument list) =>
        [.. list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
}
