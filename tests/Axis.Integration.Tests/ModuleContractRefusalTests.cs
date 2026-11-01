using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>requests</c> module's contract refuses a write on every path, and each path answers it as the
/// docs say. A task completion that moves a request along <c>submit</c> with an amount of 0 is a <c>409</c>
/// and the task stays open. A process step that does the same fails its instance, with the named point in its
/// error, and a retry after the cause is fixed runs again. An import row and an inbound endpoint call that create
/// a request as <c>approved</c>, which is not an initial state, fail as a validation does.
/// </summary>
public sealed class ModuleContractRefusalTests(ModuleContractFlowFixture fixture) : IClassFixture<ModuleContractFlowFixture>
{
    private const string App = "/api/apps/ModuleContractFlowApp";
    private const string Requests = $"{App}/entities/requests/Request";
    private const string Named = "named point Request/transitions/submit";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_task_completion_that_the_contract_refuses_is_409_and_the_task_stays_open()
    {
        var request = await fixture.InsertRequestAsync($"Task {Guid.NewGuid():N}", amount: 0, "draft");
        var (_, version) = await fixture.RequestAsync(request);
        var instance = await StartAsync("SubmitRequest", request);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var task = Assert.Single(await fixture.Steps.TasksAsync(instance));
        using var client = fixture.CreateClient().SignedInAs("requester");

        using (var refused = await SendAsync(
            client, HttpMethod.Post, $"{App}/tasks/{task.Id:D}/complete", $$"""{ "outcome": "done", "version": {{version}}, "values": { "status": "submitted" } }"""))
        {
            using var problem = await ReadProblemAsync(refused, HttpStatusCode.Conflict);
            Assert.Equal("The record's state does not allow this change.", problem.RootElement.GetProperty("title").GetString());
            Assert.Equal(
                ["/values/status"],
                problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        Assert.Equal("open", Assert.Single(await fixture.Steps.TasksAsync(instance)).State);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal(("draft", version), await fixture.RequestAsync(request));

        using var allowed = await SendAsync(
            client, HttpMethod.Post, $"{App}/tasks/{task.Id:D}/complete", $$"""{ "outcome": "done", "version": {{version}}, "values": { "status": "draft" } }""");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task A_step_that_the_contract_refuses_fails_the_instance_with_the_named_point_and_a_retry_runs_again()
    {
        var request = await fixture.InsertRequestAsync($"Step {Guid.NewGuid():N}", amount: 0, "draft");
        var instance = await StartAsync("AutoSubmit", request);

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("failed", (await fixture.Steps.InstanceAsync(instance)).State);
        using var client = fixture.CreateClient().SignedInAs("requester");
        using (var detail = await GetAsync(client, $"{App}/processes/AutoSubmit/instances/{instance:D}"))
        {
            var path = Assert.Single(detail.RootElement.GetProperty("path").EnumerateArray());
            Assert.Equal("submit", path.GetProperty("step").GetString());
            var error = path.GetProperty("error").GetString();
            Assert.Contains(Named, error, StringComparison.Ordinal);
            Assert.StartsWith("The record update was rejected: /values/status: ", error, StringComparison.Ordinal);
        }

        var (status, version) = await fixture.RequestAsync(request);
        Assert.Equal("draft", status);
        using (var fix = await SendAsync(
            client, HttpMethod.Patch, $"{Requests}/records/{request:D}", $$"""{ "version": {{version}}, "values": { "amount": 5 } }"""))
        {
            Assert.Equal(HttpStatusCode.OK, fix.StatusCode);
        }

        using (var retry = await SendAsync(
            client, HttpMethod.Post, $"{App}/processes/AutoSubmit/instances/{instance:D}/retry", """{ "reason": "The amount is set now" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        }

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal("submitted", (await fixture.RequestAsync(request)).Status);
    }

    [Fact]
    public async Task An_import_row_that_the_contract_refuses_fails_the_commit_at_the_state_field_and_writes_nothing()
    {
        var title = $"Import {Guid.NewGuid():N}";
        using var client = fixture.CreateClient().SignedInAs("requester");
        using var upload = await SendAsync(client, HttpMethod.Post, $"{Requests}/imports", $"title,status\n{title},approved\n", "text/csv");
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var id = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("id").GetGuid();
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        using (var dryRun = await GetAsync(client, $"{Requests}/imports/{id:D}"))
        {
            Assert.Equal("checked", dryRun.RootElement.GetProperty("state").GetString());
        }

        using var commit = await SendAsync(client, HttpMethod.Post, $"{Requests}/imports/{id:D}/commit", null);
        Assert.Equal(HttpStatusCode.Accepted, commit.StatusCode);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        using var failed = await GetAsync(client, $"{Requests}/imports/{id:D}");
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal(
            ["/rows/1/values/status"],
            failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(0L, await fixture.CountRequestsAsync(title));
    }

    [Fact]
    public async Task An_endpoint_call_that_the_contract_refuses_is_400_at_the_state_field_and_writes_nothing()
    {
        var title = $"Endpoint {Guid.NewGuid():N}";
        using var client = fixture.CreateClient().CallingAs("request-importer");

        using (var refused = await CallAsync(client, title, "approved"))
        {
            using var problem = await ReadProblemAsync(refused, HttpStatusCode.BadRequest);
            Assert.Equal(
                ["/values/status"],
                problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        Assert.Equal(0L, await fixture.CountRequestsAsync(title));

        using var allowed = await CallAsync(client, title, "draft");
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(1L, await fixture.CountRequestsAsync(title));
    }

    [Fact]
    public async Task A_caller_without_the_invoke_grant_cannot_call_the_endpoint_and_no_request_is_written()
    {
        var title = $"Denied {Guid.NewGuid():N}";
        using var client = fixture.CreateClient().CallingAs("outsider-system");

        using var response = await CallAsync(client, title, "draft");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0L, await fixture.CountRequestsAsync(title));
    }

    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string title, string status) =>
        SendAsync(client, HttpMethod.Post, $"{App}/endpoints/CreateRequest", $$"""{ "input": { "title": "{{title}}", "status": "{{status}}" } }""");

    private async Task<Guid> StartAsync(string process, Guid subject)
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        using var response = await SendAsync(client, HttpMethod.Post, $"{App}/processes/{process}/instances", $$"""{ "subjectId": "{{subject:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body, string contentType = "application/json") =>
        client.SendAsync(Request(method, path, ModuleContractFlowFixture.Host, body, contentType), CancellationToken);
}
