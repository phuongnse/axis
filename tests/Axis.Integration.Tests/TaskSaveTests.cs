using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskSaveTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string Records = "/api/apps/StepApp/entities/Request/records";

    /// <summary>A body that saves a <c>Decide</c> task, whose subject record is at version 1.</summary>
    private const string Valid = """{ "values": { "comment": "Fine" }, "version": 1 }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_assignee_saves_an_edited_comment_and_the_task_stays_open()
    {
        var (task, subject, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await SaveAsync(
            maria, task, TenantA, $$"""{ "values": { "comment": "Fine for Q4." }, "version": {{waiting.SubjectVersion}} }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(["version"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(waiting.SubjectVersion + 1, body.RootElement.GetProperty("version").GetInt64());

        using var record = await ReadRecordAsync(maria, subject, TenantA);
        Assert.Equal("Fine for Q4.", record.RootElement.GetProperty("values").GetProperty("comment").GetString());
        Assert.Equal(waiting.SubjectVersion + 1, record.RootElement.GetProperty("version").GetInt64());

        // Only the record's version and its audit record change: the task, the instance, its history and its work items stay.
        var saved = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal(waiting with { SubjectVersion = waiting.SubjectVersion + 1, AuditRecords = waiting.AuditRecords + 1 }, saved);
        Assert.Equal(ProcessTasks.Open, saved.TaskState);
        Assert.Equal(ProcessStarts.Waiting, saved.State);

        var actor = await fixture.ScalarAsync(
            TenantA, "SELECT actor FROM axis.audit_records WHERE record_id = @record AND action = @action", ("record", subject), ("action", AuditActions.RecordUpdated));
        Assert.Equal("maria", actor);
        var details = (string)(await fixture.ScalarAsync(
            TenantA, "SELECT details::text FROM axis.audit_records WHERE record_id = @record AND action = @action", ("record", subject), ("action", AuditActions.RecordUpdated)))!;
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "version": {{waiting.SubjectVersion + 1}}, "fields": ["comment"] }"""), JsonNode.Parse(details)));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));
    }

    [Fact]
    public async Task A_saved_task_can_still_be_completed_on_the_new_version()
    {
        var (task, subject, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var saved = await SaveAsync(
            maria, task, TenantA, $$"""{ "values": { "comment": "Draft" }, "version": {{waiting.SubjectVersion}} }""");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var completed = await CompleteAsync(
            maria, task, TenantA, $$"""{ "outcome": "approve", "values": { "comment": "Final" }, "version": {{waiting.SubjectVersion + 1}} }""");

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var record = await ReadRecordAsync(maria, subject, TenantA);
        Assert.Equal("Final", record.RootElement.GetProperty("values").GetProperty("comment").GetString());
        Assert.Equal(ProcessTasks.Completed, (await fixture.SnapshotAsync(TenantA, instance)).TaskState);
    }

    [Fact]
    public async Task A_user_who_may_not_act_on_the_task_gets_403_and_the_record_is_unchanged()
    {
        var (task, subject, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var otto = fixture.CreateClient().SignedInAs("otto");

        using var response = await SaveAsync(
            otto, task, TenantA, $$"""{ "values": { "comment": "Fine" }, "version": {{waiting.SubjectVersion}} }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("This task is assigned to someone else.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(waiting, await fixture.SnapshotAsync(TenantA, instance));
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var record = await ReadRecordAsync(maria, subject, TenantA);
        Assert.Equal(JsonValueKind.Null, record.RootElement.GetProperty("values").GetProperty("comment").ValueKind);
    }

    [Fact]
    public async Task A_save_on_a_completed_task_is_409_with_no_keys_and_the_record_is_unchanged()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var completed = await CompleteAsync(maria, task, TenantA, """{ "outcome": "reject" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var decided = await fixture.SnapshotAsync(TenantA, instance);

        using var response = await SaveAsync(
            maria, task, TenantA, $$"""{ "values": { "comment": "Late" }, "version": {{waiting.SubjectVersion}} }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("This task is already completed.", problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(decided, await fixture.SnapshotAsync(TenantA, instance));
        Assert.Equal(waiting.SubjectVersion, decided.SubjectVersion);
    }

    [Fact]
    public async Task A_save_with_a_stale_version_is_409_at_version_and_the_record_is_unchanged()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await SaveAsync(maria, task, TenantA, """{ "values": { "comment": "Fine" }, "version": 99 }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("The record has changed since this version was read.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(["/version"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(waiting, await fixture.SnapshotAsync(TenantA, instance));
    }

    [Theory]
    [InlineData(null, Valid, "application/json", HttpStatusCode.Unauthorized, "Sign in to read or write this data.", "")]
    [InlineData("maria", Valid, "text/plain", HttpStatusCode.UnsupportedMediaType, "The request body must be application/json.", "")]
    [InlineData("maria", """{ "values": { "title": "New" }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/title")]
    [InlineData("maria", """{ "values": { "amount": 5 }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/amount")]
    [InlineData("maria", """{ "values": { "comment": "Fine", "title": "New" }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/title")]
    [InlineData("maria", """{ "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values")]
    [InlineData("maria", """{ "values": {}, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values")]
    [InlineData("maria", """{ "values": [], "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values")]
    [InlineData("maria", """{ "values": { "comment": "Fine" } }""", "application/json", HttpStatusCode.BadRequest, null, "/version")]
    [InlineData("maria", """{ "values": { "title": "New" } }""", "application/json", HttpStatusCode.BadRequest, null, "/values/title,/version")]
    [InlineData("maria", """{ "outcome": "approve", "values": { "comment": "Fine" }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/outcome")]
    [InlineData("maria", """{ "values": { "comment": 5 }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/comment")]
    [InlineData("maria", "[]", "application/json", HttpStatusCode.BadRequest, null, "")]
    public async Task A_rejected_save_changes_nothing(
        string? user, string body, string contentType, HttpStatusCode status, string? title, string keys)
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var client = user is null ? fixture.CreateClient() : fixture.CreateClient().SignedInAs(user);

        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/save", Host(TenantA), body, contentType);
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, status);
        if (title is not null)
        {
            Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        }

        if (keys.Length > 0)
        {
            Assert.Equal(keys.Split(','), problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        }

        Assert.Equal(waiting, await fixture.SnapshotAsync(TenantA, instance));
    }

    [Fact]
    public async Task The_messages_of_a_bad_save_are_the_record_apis()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "Decide");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await SaveAsync(maria, task, TenantA, """{ "values": { "title": "New" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Cannot be set."], errors.GetProperty("/values/title").EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(["Required."], errors.GetProperty("/version").EnumerateArray().Select(message => message.GetString()));
    }

    [Theory]
    [InlineData("/api/apps/NoSuchApp/tasks/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/save", "No application is active under this name.")]
    [InlineData(Tasks + "/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/save", "No task exists with this id.")]
    [InlineData(Tasks + "/not-a-uuid/save", "No task exists with this id.")]
    public async Task An_unknown_application_or_task_is_404(string path, string title)
    {
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var request = RecordApiFixture.Request(HttpMethod.Post, path, Host(TenantA), Valid);
        using var response = await maria.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// Creates a request in a department managed by <paramref name="manager"/>, starts
    /// <paramref name="process"/> for it and runs the tenant's work items, so the instance waits on
    /// its task. Returns the task's id, the request's id and the instance's id.
    /// </summary>
    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string tenant, string process, string manager = "maria")
    {
        var department = await fixture.InsertDepartmentAsync(tenant, manager);
        var subject = await fixture.InsertRequestAsync(tenant, department);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return (await fixture.TaskIdAsync(tenant, instance), subject, instance);
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid task, string tenant, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/save", Host(tenant), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid task, string tenant, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(tenant), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadRecordAsync(HttpClient client, Guid id, string tenant)
    {
        using var request = RecordApiFixture.Request($"{Records}/{id:D}", Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
