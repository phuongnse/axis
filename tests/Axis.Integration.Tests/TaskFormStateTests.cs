using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskFormStateTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private const string CommentTouched = """{ "values": { "comment": null }, "touched": ["comment"] }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_assignee_gets_the_field_states_and_a_required_message_for_a_touched_empty_comment()
    {
        var task = await CreateTaskAsync(amount: 2000m);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await FormStateAsync(maria, task, CommentTouched);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        var fields = root.GetProperty("fields");
        Assert.Equal(["title", "amount", "comment"], fields.EnumerateObject().Select(field => field.Name));
        AssertState(fields.GetProperty("title"), visible: true, required: true, readOnly: true);
        AssertState(fields.GetProperty("amount"), visible: true, required: false, readOnly: true);
        AssertState(fields.GetProperty("comment"), visible: true, required: true, readOnly: false);
        var errors = root.GetProperty("errors");
        Assert.Equal(["/values/comment"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(["Required."], errors.GetProperty("/values/comment").EnumerateArray().Select(message => message.GetString()));
    }

    [Fact]
    public async Task A_comment_is_required_only_while_the_amount_is_over_the_limit_and_the_comment_is_empty()
    {
        var small = await CreateTaskAsync(amount: 500m);
        var large = await CreateTaskAsync(amount: 2000m);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using (var response = await FormStateAsync(maria, small, CommentTouched))
        {
            using var body = await ReadJsonAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(body.RootElement.GetProperty("fields").GetProperty("comment").GetProperty("required").GetBoolean());
            Assert.Empty(body.RootElement.GetProperty("errors").EnumerateObject());
        }

        using (var response = await FormStateAsync(maria, large, """{ "values": { "comment": "Fine" }, "touched": ["comment"] }"""))
        {
            using var body = await ReadJsonAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(body.RootElement.GetProperty("fields").GetProperty("comment").GetProperty("required").GetBoolean());
            Assert.Empty(body.RootElement.GetProperty("errors").EnumerateObject());
        }
    }

    [Fact]
    public async Task An_assignee_who_may_not_read_the_comment_gets_no_entry_for_it()
    {
        var task = await CreateTaskAsync(manager: "rita", amount: 2000m);
        using var rita = fixture.CreateClient().SignedInAs("rita");

        using var response = await FormStateAsync(rita, task, CommentTouched);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(["title"], body.RootElement.GetProperty("fields").EnumerateObject().Select(field => field.Name));
        Assert.Empty(body.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Theory]
    [InlineData("anonymous", "application/json", """{ "values": {}, "touched": [] }""", HttpStatusCode.Unauthorized, null)]
    [InlineData("fiona", "application/json", """{ "values": {}, "touched": [] }""", HttpStatusCode.Forbidden, null)]
    [InlineData("maria", "text/plain", """{ "values": {}, "touched": [] }""", HttpStatusCode.UnsupportedMediaType, null)]
    [InlineData("maria", "application/json", """{ "recordId": null, "values": {}, "touched": [] }""", HttpStatusCode.BadRequest, "/recordId")]
    [InlineData("maria", "application/json", """{ "values": {} }""", HttpStatusCode.BadRequest, "/touched")]
    public async Task A_request_that_fails_a_check_gets_the_status_of_that_check(
        string user, string contentType, string body, HttpStatusCode status, string? key)
    {
        var task = await CreateTaskAsync(amount: 2000m);
        using var client = user == "anonymous" ? fixture.CreateClient() : fixture.CreateClient().SignedInAs(user);

        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/form-state", Host(TenantA), body, contentType);
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, status);
        if (key is not null)
        {
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
            if (key == "/recordId")
            {
                Assert.Equal(["Unknown property."], errors.GetProperty(key).EnumerateArray().Select(message => message.GetString()));
            }
        }
    }

    [Fact]
    public async Task An_unknown_task_is_not_found()
    {
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await FormStateAsync(maria, Guid.NewGuid(), CommentTouched);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal("No task exists with this id.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_completed_task_is_a_conflict_for_its_assignee()
    {
        var task = await CreateTaskAsync(amount: 2000m);
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using (var complete = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(TenantA), """{ "outcome": "reject" }"""))
        {
            using var completed = await maria.SendAsync(complete, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }

        using var response = await FormStateAsync(maria, task, CommentTouched);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("This task is already completed.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_user_who_is_not_the_assignee_is_told_so()
    {
        var task = await CreateTaskAsync(amount: 2000m);
        using var fiona = fixture.CreateClient().SignedInAs("fiona");

        using var response = await FormStateAsync(fiona, task, CommentTouched);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("This task is assigned to someone else.", problem.RootElement.GetProperty("title").GetString());
    }

    private static void AssertState(JsonElement state, bool visible, bool required, bool readOnly)
    {
        Assert.Equal(visible, state.GetProperty("visible").GetBoolean());
        Assert.Equal(required, state.GetProperty("required").GetBoolean());
        Assert.Equal(readOnly, state.GetProperty("readOnly").GetBoolean());
    }

    /// <summary>Starts a <c>Decide</c> process on a request with <paramref name="amount"/> and returns its open task.</summary>
    private async Task<Guid> CreateTaskAsync(string manager = "maria", decimal? amount = null)
    {
        var department = await fixture.InsertDepartmentAsync(TenantA, manager);
        var subject = await fixture.InsertRequestAsync(TenantA, department, amount: amount);
        var instance = await fixture.StartAsync(TenantA, "Decide", subject);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        return await fixture.TaskIdAsync(TenantA, instance);
    }

    private static async Task<HttpResponseMessage> FormStateAsync(HttpClient client, Guid task, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/form-state", Host(TenantA), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
