using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskCompletionTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string Records = "/api/apps/StepApp/entities/Request/records";

    /// <summary>A body that completes a <c>Decide</c> task, whose subject record is at version 1.</summary>
    private const string Valid = """{ "outcome": "approve", "values": { "comment": "Fine" }, "version": 1 }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_assignee_completes_with_approve_and_an_edited_comment_and_the_worker_moves_the_instance_on()
    {
        var (task, subject, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = await SignInAsync("maria", TenantA);

        using var response = await CompleteAsync(
            maria, task, TenantA, $$"""{ "outcome": "Approve", "values": { "comment": "Fine for Q4." }, "version": {{waiting.SubjectVersion}} }""");

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
        Assert.Equal("completed", root.GetProperty("state").GetString());
        Assert.Equal("approve", root.GetProperty("outcome").GetString());
        Assert.Equal("maria", root.GetProperty("completedBy").GetString());
        Assert.EndsWith("Z", root.GetProperty("completedAt").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("completedAt").GetDateTimeOffset() >= root.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal("RequestDecision", root.GetProperty("form").GetProperty("name").GetString());

        using var record = await ReadRecordAsync(maria, subject, TenantA);
        Assert.Equal("Fine for Q4.", record.RootElement.GetProperty("values").GetProperty("comment").GetString());
        Assert.Equal(waiting.SubjectVersion + 1, record.RootElement.GetProperty("version").GetInt64());

        var resumed = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal(waiting with
        {
            State = ProcessStarts.Running,
            Step = "approved",
            Revision = waiting.Revision + 1,
            TaskState = ProcessTasks.Completed,
            AuditRecords = waiting.AuditRecords + 1,
            HistoryRows = waiting.HistoryRows + 1,
            WorkItems = 1,
            SubjectVersion = waiting.SubjectVersion + 1,
        }, resumed);

        var (actor, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));
        Assert.Equal("maria", actor);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "outcome": "approve", "fields": ["comment"] }"""), JsonNode.Parse(details)));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.RecordUpdated));

        var history = await fixture.HistoryAsync(TenantA, instance);
        Assert.Equal(["decide", "decide"], history.Select(row => row.Step));
        var decided = history[1];
        Assert.Equal(waiting.Revision, decided.Revision);
        Assert.Null(decided.Error);
        Assert.True(decided.FinishedAfterStart);
        Assert.Equal("approve", decided.Decision);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "subjectId": "{{subject:D}}", "subjectVersion": {{waiting.SubjectVersion}} }"""),
            JsonNode.Parse(decided.Input)));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "outcome": "approve", "next": "approved", "subjectVersion": {{waiting.SubjectVersion + 1}} }"""),
            JsonNode.Parse(decided.Output!)));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        var ended = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal(ProcessStarts.Completed, ended.State);
        Assert.Equal("approved", ended.Step);
        Assert.Equal(0, ended.WorkItems);
    }

    [Fact]
    public async Task A_completion_without_values_takes_its_outcome_and_leaves_the_record_unchanged()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = await SignInAsync("maria", TenantA);

        using var response = await CompleteAsync(maria, task, TenantA, """{ "outcome": "reject" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("reject", body.RootElement.GetProperty("outcome").GetString());
        var (_, details) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "outcome": "reject", "fields": [] }"""), JsonNode.Parse(details)));
        var decided = (await fixture.HistoryAsync(TenantA, instance))[^1];
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "taskId": "{{task:D}}", "outcome": "reject", "next": "rejected" }"""),
            JsonNode.Parse(decided.Output!)));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        var ended = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal(ProcessStarts.Completed, ended.State);
        Assert.Equal("rejected", ended.Step);
        Assert.Equal(waiting.SubjectVersion, ended.SubjectVersion);
    }

    [Fact]
    public async Task A_role_holder_completes_the_role_task()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "RoleApprove");
        using var fiona = await SignInAsync("fiona", TenantA);

        using var response = await CompleteAsync(fiona, task, TenantA, """{ "outcome": "approve" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (actor, _) = Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));
        Assert.Equal("fiona", actor);
    }

    [Fact]
    public async Task Two_concurrent_completions_store_exactly_one_decision()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var first = await SignInAsync("maria", TenantA);
        using var second = await SignInAsync("maria", TenantA);

        var responses = await Task.WhenAll(
            CompleteAsync(first, task, TenantA, $$"""{ "outcome": "approve", "values": { "comment": "First" }, "version": {{waiting.SubjectVersion}} }"""),
            CompleteAsync(second, task, TenantA, """{ "outcome": "reject" }"""));
        try
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).Order());
            var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            using var problem = await RecordApiFixture.ReadProblemAsync(loser, HttpStatusCode.Conflict);
            Assert.Equal("This task is already completed.", problem.RootElement.GetProperty("title").GetString());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var completions = await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted);
        Assert.Single(completions);
        var decisions = (await fixture.HistoryAsync(TenantA, instance)).Where(row => row.Decision is not null).ToList();
        var decision = Assert.Single(decisions);
        Assert.Contains($"\"{decision.Decision}\"", completions[0].Details, StringComparison.Ordinal);

        var resumed = await fixture.SnapshotAsync(TenantA, instance);
        Assert.Equal(waiting.Revision + 1, resumed.Revision);
        Assert.Equal(ProcessTasks.Completed, resumed.TaskState);
        Assert.Equal(1, resumed.WorkItems);

        using var again = await CompleteAsync(first, task, TenantA, """{ "outcome": "approve" }""");
        using var conflict = await RecordApiFixture.ReadProblemAsync(again, HttpStatusCode.Conflict);
        Assert.Equal(resumed, await fixture.SnapshotAsync(TenantA, instance));
    }

    [Theory]
    [InlineData(null, Valid, "application/json", HttpStatusCode.Unauthorized, "Sign in to work on tasks.", "")]
    [InlineData("otto", Valid, "application/json", HttpStatusCode.Forbidden, "This task is assigned to someone else.", "")]
    [InlineData("maria", Valid, "text/plain", HttpStatusCode.UnsupportedMediaType, "The request body must be application/json.", "")]
    [InlineData("maria", """{ "outcome": "nope" }""", "application/json", HttpStatusCode.BadRequest, null, "/outcome")]
    [InlineData("maria", """{ "values": {} }""", "application/json", HttpStatusCode.BadRequest, null, "/outcome")]
    [InlineData("maria", """{ "outcome": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/outcome")]
    [InlineData("maria", """{ "outcome": "approve", "values": { "title": "New" }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/title")]
    [InlineData("maria", """{ "outcome": "approve", "values": { "amount": 5 }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/amount")]
    [InlineData("maria", """{ "outcome": "nope", "values": { "comment": "Fine", "title": "New" }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/outcome,/values/title")]
    [InlineData("maria", """{ "outcome": "approve", "values": { "comment": 5 }, "version": 1 }""", "application/json", HttpStatusCode.BadRequest, null, "/values/comment")]
    [InlineData("maria", """{ "outcome": "approve", "values": { "comment": "Fine" } }""", "application/json", HttpStatusCode.BadRequest, null, "/version")]
    [InlineData("maria", """{ "outcome": "approve", "comment": "Fine" }""", "application/json", HttpStatusCode.BadRequest, null, "/comment")]
    [InlineData("maria", """{ "outcome": "approve", "values": [] }""", "application/json", HttpStatusCode.BadRequest, null, "/values")]
    [InlineData("maria", "[]", "application/json", HttpStatusCode.BadRequest, null, "")]
    [InlineData("maria", """{ "outcome": "approve", "values": { "comment": "Fine" }, "version": 99 }""", "application/json", HttpStatusCode.Conflict, "The record has changed since this version was read.", "/version")]
    public async Task A_rejected_completion_changes_nothing(
        string? user, string body, string contentType, HttpStatusCode status, string? title, string keys)
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var client = user is null ? fixture.CreateClient() : await SignInAsync(user, TenantA);

        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(TenantA), body, contentType);
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
        Assert.Equal(ProcessTasks.Open, waiting.TaskState);
        Assert.Equal(ProcessStarts.Waiting, waiting.State);
        Assert.Equal(0, waiting.WorkItems);
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, AuditActions.TaskCompleted));
    }

    [Fact]
    public async Task An_unlisted_or_read_only_field_and_an_unknown_outcome_are_reported_with_their_messages()
    {
        var (task, _, _) = await CreateTaskAsync(TenantA, "Decide");
        using var maria = await SignInAsync("maria", TenantA);

        using var response = await CompleteAsync(
            maria, task, TenantA, """{ "outcome": "nope", "values": { "title": "New", "amount": 5 }, "version": 1 }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be one of the step's outcomes."], errors.GetProperty("/outcome").EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(["Cannot be set."], errors.GetProperty("/values/title").EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(["Cannot be set."], errors.GetProperty("/values/amount").EnumerateArray().Select(message => message.GetString()));
    }

    [Theory]
    [InlineData("/api/apps/NoSuchApp/tasks/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/complete", "No application is active under this name.")]
    [InlineData(Tasks + "/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/complete", "No task exists with this id.")]
    [InlineData(Tasks + "/not-a-uuid/complete", "No task exists with this id.")]
    public async Task An_unknown_application_or_task_is_404(string path, string title)
    {
        using var maria = await SignInAsync("maria", TenantA);

        using var request = RecordApiFixture.Request(HttpMethod.Post, path, Host(TenantA), Valid);
        using var response = await maria.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_task_of_one_tenant_cannot_be_completed_on_another_tenants_host()
    {
        var (task, _, instance) = await CreateTaskAsync(TenantA, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        using var maria = await SignInAsync("maria", TenantB);

        using var response = await CompleteAsync(maria, task, TenantB, """{ "outcome": "approve" }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(waiting, await fixture.SnapshotAsync(TenantA, instance));
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

    private async Task<HttpClient> SignInAsync(string user, string tenant)
    {
        var client = fixture.CreateClient();
        using var signIn = RecordApiFixture.Request(HttpMethod.Post, "/api/test-users/sign-in", Host(tenant), $$"""{ "id": "{{user}}" }""");
        using var response = await client.SendAsync(signIn, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
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
