using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.EndpointOutputFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Endpoints with <c>output</c> and <c>wait</c>: the call waits for its instance after the commit,
/// and the status location reports the instance to the caller who started it. EndpointApp's
/// <c>ImportClosedNote</c> closes the note at once, <c>ImportReviewedNote</c> and
/// <c>ImportRepeatedNote</c> wait on a task for the role <c>importer</c>, and <c>ImportFailingNote</c> breaks a validation and fails.
/// </summary>
public sealed class EndpointOutputTests(EndpointOutputFixture fixture) : IClassFixture<EndpointOutputFixture>
{
    private const string Endpoints = "/api/apps/EndpointApp/endpoints";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_process_that_completes_within_the_wait_answers_201_with_exactly_the_output_fields()
    {
        using var response = await CallWithWorkerAsync("ImportClosedNote", """{ "input": { "title": "Closed at once", "quantity": 3 } }""");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        Assert.Null(response.Headers.Location);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(["recordId", "instanceId", "state", "output"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("completed", root.GetProperty("state").GetString());
        var output = root.GetProperty("output");
        Assert.Equal(["title", "status"], output.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Closed at once", output.GetProperty("title").GetString());
        Assert.Equal("closed", output.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_process_that_waits_on_a_task_answers_202_and_its_status_location_reports_the_output_once_completed()
    {
        using var response = await CallWithWorkerAsync("ImportReviewedNote", """{ "input": { "title": "Needs review" } }""");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(["recordId", "instanceId", "state"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("waiting", root.GetProperty("state").GetString());
        var recordId = root.GetProperty("recordId").GetGuid();
        var instanceId = root.GetProperty("instanceId").GetGuid();
        var location = response.Headers.Location?.ToString();
        Assert.Equal($"{Endpoints}/ImportReviewedNote/instances/{instanceId:D}", location);

        using (var waiting = await GetAsync(location))
        {
            using var status = await ReadJsonAsync(waiting, HttpStatusCode.OK);
            Assert.Equal(["recordId", "instanceId", "state"], status.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal(recordId, status.RootElement.GetProperty("recordId").GetGuid());
            Assert.Equal(instanceId, status.RootElement.GetProperty("instanceId").GetGuid());
            Assert.Equal("waiting", status.RootElement.GetProperty("state").GetString());
        }

        var task = await fixture.TaskIdAsync(instanceId);
        using (var ivy = fixture.CreateClient().SignedInAs("ivy"))
        using (var complete = RecordApiFixture.Request(HttpMethod.Post, $"/api/apps/EndpointApp/tasks/{task:D}/complete", Host, """{ "outcome": "accept" }"""))
        using (var completed = await ivy.SendAsync(complete, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);

        using var done = await GetAsync(location);
        using var final = await ReadJsonAsync(done, HttpStatusCode.OK);
        var finalRoot = final.RootElement;
        Assert.Equal(["recordId", "instanceId", "state", "output"], finalRoot.EnumerateObject().Select(property => property.Name));
        Assert.Equal("completed", finalRoot.GetProperty("state").GetString());
        var output = finalRoot.GetProperty("output");
        Assert.Equal(["title", "status"], output.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Needs review", output.GetProperty("title").GetString());
        Assert.Equal("open", output.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_status_location_is_403_without_the_grant_and_404_for_another_caller_another_endpoint_or_an_unknown_instance()
    {
        using var response = await CallAsync("ImportReviewedNote", """{ "input": { "title": "Private call" } }""");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        var instanceId = location[(location.LastIndexOf('/') + 1)..];

        using (var own = await GetAsync(location))
        {
            using var _ = await ReadJsonAsync(own, HttpStatusCode.OK);
        }

        using var noCaller = await GetAsync(location, caller: null);
        await AssertProblemAsync(noCaller, HttpStatusCode.Unauthorized, "A valid bearer token is needed to call this endpoint.");
        using var reader = await GetAsync(location, caller: "reader-system");
        await AssertProblemAsync(reader, HttpStatusCode.Forbidden, "You have no grant for this action.");
        using var readerUnknown = await GetAsync($"{Endpoints}/ImportReviewedNote/instances/{Guid.CreateVersion7():D}", caller: "reader-system");
        await AssertProblemAsync(readerUnknown, HttpStatusCode.Forbidden, "You have no grant for this action.");

        using var other = await GetAsync(location, caller: "other-importer");
        await AssertProblemAsync(other, HttpStatusCode.NotFound, "No instance of this endpoint exists with this id.");
        using var otherEndpoint = await GetAsync($"{Endpoints}/ImportClosedNote/instances/{instanceId}");
        await AssertProblemAsync(otherEndpoint, HttpStatusCode.NotFound, "No instance of this endpoint exists with this id.");
        using var unknown = await GetAsync($"{Endpoints}/ImportReviewedNote/instances/{Guid.CreateVersion7():D}");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "No instance of this endpoint exists with this id.");
        using var invalid = await GetAsync($"{Endpoints}/ImportReviewedNote/instances/not-a-uuid");
        await AssertProblemAsync(invalid, HttpStatusCode.NotFound, "No instance of this endpoint exists with this id.");
        using var unknownEndpoint = await GetAsync($"{Endpoints}/ImportMissing/instances/{instanceId}");
        await AssertProblemAsync(unknownEndpoint, HttpStatusCode.NotFound, "No endpoint exists with this name.");
    }

    [Fact]
    public async Task A_waiting_call_holds_no_connection_so_its_status_location_answers_from_a_pool_of_one()
    {
        const string title = "Pool of one";
        var call = CallAsync("ImportReviewedNote", $$"""{ "input": { "title": "{{title}}" } }""");

        Guid? instanceId;
        while ((instanceId = await fixture.FindInstanceAsync(title)) is null)
        {
            Assert.False(call.IsCompleted, "The call ended before its instance was found.");
            await Task.Delay(20, CancellationToken);
        }

        // The server's pool holds one connection, so this read waits for any connection the call keeps.
        using (var status = await GetAsync($"{Endpoints}/ImportReviewedNote/instances/{instanceId:D}"))
        {
            Assert.False(call.IsCompleted, "The status location answered only after the call ended.");
            using var json = await ReadJsonAsync(status, HttpStatusCode.OK);
            Assert.Equal("running", json.RootElement.GetProperty("state").GetString());
        }

        using var response = await call;
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_instance_ends_the_wait_with_202_and_no_output_and_its_status_location_answers_the_same()
    {
        // The endpoint waits 30 seconds, so a call that answers within the 9-second deadline is 3.3 times
        // short of its wait.
        var call = CallAsync("ImportFailingNote", """{ "input": { "title": "Bound to fail", "quantity": 2 } }""");
        await Polling.WaitUntilAsync(
            async () =>
            {
                await fixture.RunUntilIdleAsync(CancellationToken);
                return call.IsCompleted;
            },
            "The call kept waiting for its failed instance.",
            TimeSpan.FromSeconds(9));
        using var response = await call;

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(["recordId", "instanceId", "state"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("failed", json.RootElement.GetProperty("state").GetString());
        var instanceId = json.RootElement.GetProperty("instanceId").GetGuid();
        var location = response.Headers.Location?.ToString();
        Assert.Equal($"{Endpoints}/ImportFailingNote/instances/{instanceId:D}", location);

        using var status = await GetAsync(location);
        using var statusJson = await ReadJsonAsync(status, HttpStatusCode.OK);
        Assert.Equal(["recordId", "instanceId", "state"], statusJson.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("failed", statusJson.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_repeat_never_waits_and_answers_202_until_the_instance_completes_then_201_with_the_output()
    {
        const string Key = "reviewed-repeat";
        const string Title = "Repeat review";
        const string Body = """{ "input": { "title": "Repeat review" } }""";
        var first = CallAsync("ImportRepeatedNote", Body, Key);

        Guid? found;
        while ((found = await fixture.FindInstanceAsync(Title)) is null)
        {
            Assert.False(first.IsCompleted, "The first call ended before its instance was found.");
            await Task.Delay(20, CancellationToken);
        }

        var instanceId = found.Value;
        await fixture.RunUntilIdleAsync(CancellationToken);

        // The endpoint waits up to 30 seconds. A repeat answers while the first call still waits.
        Guid recordId;
        using (var waiting = await CallAsync("ImportRepeatedNote", Body, Key))
        {
            Assert.False(first.IsCompleted, "The repeat answered only after the first call stopped waiting.");
            using var json = await ReadJsonAsync(waiting, HttpStatusCode.Accepted);
            Assert.Equal(["recordId", "instanceId", "state"], json.RootElement.EnumerateObject().Select(property => property.Name));
            recordId = json.RootElement.GetProperty("recordId").GetGuid();
            Assert.Equal(instanceId, json.RootElement.GetProperty("instanceId").GetGuid());
            Assert.Equal("waiting", json.RootElement.GetProperty("state").GetString());
            Assert.Equal($"{Endpoints}/ImportRepeatedNote/instances/{instanceId:D}", waiting.Headers.Location?.ToString());
        }

        var task = await fixture.TaskIdAsync(instanceId);
        using (var ivy = fixture.CreateClient().SignedInAs("ivy"))
        using (var complete = RecordApiFixture.Request(HttpMethod.Post, $"/api/apps/EndpointApp/tasks/{task:D}/complete", Host, """{ "outcome": "accept" }"""))
        using (var completed = await ivy.SendAsync(complete, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);

        using var firstResponse = await first;
        using var firstJson = await ReadJsonAsync(firstResponse, HttpStatusCode.Created);
        Assert.Equal("completed", firstJson.RootElement.GetProperty("state").GetString());
        Assert.Equal(recordId, firstJson.RootElement.GetProperty("recordId").GetGuid());

        using var done = await CallAsync("ImportRepeatedNote", Body, Key);
        using var doneJson = await ReadJsonAsync(done, HttpStatusCode.Created);
        var root = doneJson.RootElement;
        Assert.Null(done.Headers.Location);
        Assert.Equal(recordId, root.GetProperty("recordId").GetGuid());
        Assert.Equal(instanceId, root.GetProperty("instanceId").GetGuid());
        Assert.Equal("completed", root.GetProperty("state").GetString());
        Assert.Equal("Repeat review", root.GetProperty("output").GetProperty("title").GetString());
        Assert.Equal("open", root.GetProperty("output").GetProperty("status").GetString());
    }

    /// <summary>Sends a call and runs the worker until the call answers.</summary>
    private async Task<HttpResponseMessage> CallWithWorkerAsync(string endpoint, string body, string? key = null)
    {
        var call = CallAsync(endpoint, body, key);
        while (!call.IsCompleted)
        {
            await fixture.RunUntilIdleAsync(CancellationToken);
            await Task.Delay(50, CancellationToken);
        }

        return await call;
    }

    /// <summary>
    /// Sends a call to <paramref name="endpoint"/> as <c>importer-system</c>, without the CSRF header,
    /// with <paramref name="key"/> as its <c>Idempotency-Key</c> unless it is null.
    /// </summary>
    private async Task<HttpResponseMessage> CallAsync(string endpoint, string body, string? key = null)
    {
        using var client = fixture.CreateClient().CallingAs("importer-system");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{Endpoints}/{endpoint}", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = Host;
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    /// <summary>Reads <paramref name="path"/> as <paramref name="caller"/>, or as nobody when it is null.</summary>
    private async Task<HttpResponseMessage> GetAsync(string? path, string? caller = "importer-system")
    {
        Assert.NotNull(path);
        using var client = fixture.CreateClient();
        if (caller is not null)
        {
            client.CallingAs(caller);
        }

        using var request = RecordApiFixture.Request(path, Host);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == statusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(body);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == statusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }
}
