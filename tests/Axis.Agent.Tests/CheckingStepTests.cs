using System.Net;
using System.Text.Json.Nodes;
using Axis.Tenancy.Realms;

namespace Axis.Agent.Tests;

/// <summary>
/// The checking step compiles the draft, runs every scenario on it when it compiles and moves the request to <c>reviewing</c> or <c>fixing</c>, see
/// <c>docs/reference/authoring-requests.md#states</c>. A run that does not end within the allowed polls stops the request, see
/// <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
public sealed class CheckingStepTests
{
    private static readonly Guid _applicationId = Guid.Parse("0f0c3b6e-3d1f-4d5e-9a57-6c1f4b0f2a11");
    private static readonly Guid _draftId = Guid.Parse("7d1a5c52-0c1e-4b57-8c07-9f0d9d1e5b33");
    private const string RunId = "0192c5a0-0000-7000-8000-000000000001";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string Message(int id, string structuredContent) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "answer" }),
                ["structuredContent"] = JsonNode.Parse(structuredContent),
                ["isError"] = false,
            },
        }.ToJsonString();

    // The session opening, then list_applications, which the step reads first.
    private static RecordingHandler Opened(RecordingHandler handler) =>
        handler
            .Answer(HttpStatusCode.OK, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["result"] = new JsonObject { ["protocolVersion"] = McpProtocol.Version, ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() } },
            }.ToJsonString(), response => response.Headers.Add("Mcp-Session-Id", "session-1"))
            .Answer(HttpStatusCode.Accepted)
            .Answer(HttpStatusCode.OK, Message(2, $$"""{"items":[{"id":"{{_applicationId}}","name":"PurchaseRequests"}]}"""));

    private static AuthoringStepContext NewContext(RecordingHandler handler, Guid? draft = null)
    {
        var client = new AuthorMcpClient(new HttpClient(handler), "a.example.test", _ => Task.FromResult("token"));
        var request = new AuthoringRequestToWork(Guid.NewGuid(), "checking", DateTimeOffset.UtcNow, _applicationId, "Add a review", "A plan", null, [], draft ?? _draftId, null);
        return new AuthoringStepContext(
            "tenant",
            request,
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult("token"),
            (_, _) => Task.CompletedTask,
            client);
    }

    private static IEnumerable<string> Tools(RecordingHandler handler) =>
        handler.Requests
            .Select(request => JsonNode.Parse(request.Body)!)
            .Where(message => message["method"]!.GetValue<string>() == "tools/call")
            .Select(message => message["params"]!["name"]!.GetValue<string>());

    [Fact]
    public async Task A_compile_error_records_the_scenarios_as_notRun_starts_no_run_and_moves_the_request_to_fixing()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, Message(3, """{"diagnostics":[{"code":"AX1","severity":"error","message":"broken","file":"a.json","path":"","resourceId":null}]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("fixing", result.NextState);
        Assert.Equal("failed", result.Outcome);
        var checks = JsonNode.Parse(result.Checks!)!;
        Assert.Equal("failed", checks["compile"]!["outcome"]!.GetValue<string>());
        Assert.Equal("AX1", checks["compile"]!["diagnostics"]![0]!["code"]!.GetValue<string>());
        Assert.Equal("notRun", checks["scenarios"]!["outcome"]!.GetValue<string>());
        Assert.Null(checks["scenarios"]!["request"]);
        Assert.Empty(checks["scenarios"]!["items"]!.AsArray());
        Assert.Equal(["list_applications", "validate_draft"], Tools(handler));
        Assert.DoesNotContain(handler.Requests, request => request.Body.Contains("run_scenarios", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_warning_does_not_fail_the_compile_and_a_succeeded_run_moves_the_request_to_reviewing()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, Message(3, """{"diagnostics":[{"code":"AX2","severity":"warning","message":"odd","file":"a.json","path":"","resourceId":null}]}"""))
            .Answer(HttpStatusCode.OK, Message(4, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(5, $$"""{"request":"{{RunId}}","status":"running","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(6, $$"""{"request":"{{RunId}}","status":"succeeded","diagnostics":[],"scenarios":[{"name":"A","outcome":"passed"}]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        Assert.Equal("passed", result.Outcome);
        var checks = JsonNode.Parse(result.Checks!)!;
        Assert.Equal("passed", checks["compile"]!["outcome"]!.GetValue<string>());
        Assert.Equal("warning", checks["compile"]!["diagnostics"]![0]!["severity"]!.GetValue<string>());
        Assert.Equal("passed", checks["scenarios"]!["outcome"]!.GetValue<string>());
        Assert.Equal(RunId, checks["scenarios"]!["request"]!.GetValue<string>());
        Assert.Equal("A", checks["scenarios"]!["items"]![0]!["name"]!.GetValue<string>());
        var starts = handler.Requests.Select(request => JsonNode.Parse(request.Body)!).Where(message => message["params"]?["name"]?.GetValue<string>() == "run_scenarios").ToList();
        Assert.Null(starts[0]["params"]!["arguments"]!["request"]);
        Assert.Null(starts[0]["params"]!["arguments"]!["names"]);
        Assert.Equal(RunId, starts[1]["params"]!["arguments"]!["request"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_failed_run_keeps_its_items_and_moves_the_request_to_fixing()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, Message(3, """{"diagnostics":[]}"""))
            .Answer(HttpStatusCode.OK, Message(4, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(5, $$"""{"request":"{{RunId}}","status":"failed","diagnostics":[],"scenarios":[{"name":"m/A","outcome":"failed","step":"expectInstance"}]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("fixing", result.NextState);
        Assert.Equal("failed", result.Outcome);
        var checks = JsonNode.Parse(result.Checks!)!;
        Assert.Equal("passed", checks["compile"]!["outcome"]!.GetValue<string>());
        Assert.Equal("failed", checks["scenarios"]!["outcome"]!.GetValue<string>());
        Assert.Equal("expectInstance", checks["scenarios"]!["items"]![0]!["step"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_run_that_is_still_queued_after_the_allowed_polls_stops_the_request_as_failed_and_unfinished()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, Message(3, """{"diagnostics":[]}"""))
            .Answer(HttpStatusCode.OK, Message(4, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(5, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(6, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("failed", result.NextState);
        Assert.Equal("unfinished", result.Outcome);
        Assert.Null(result.Checks);
        Assert.Equal(["list_applications", "validate_draft", "run_scenarios", "run_scenarios", "run_scenarios"], Tools(handler));
    }

    [Fact]
    public async Task A_request_without_a_draft_stops_as_failed_and_unfinished_before_any_check()
    {
        var handler = Opened(new RecordingHandler());
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);
        var request = new AuthoringRequestToWork(Guid.NewGuid(), "checking", DateTimeOffset.UtcNow, _applicationId, "Add a review", "A plan", null, [], null, null);
        var client = new AuthorMcpClient(new HttpClient(handler), "a.example.test", _ => Task.FromResult("token"));
        var context = new AuthoringStepContext("tenant", request, (_, _) => Task.CompletedTask, _ => Task.FromResult("token"), (_, _) => Task.CompletedTask, client);

        var result = await step.RunAsync(context, CancellationToken);

        Assert.Equal(new AuthoringStepResult("failed", "unfinished"), result);
        Assert.Equal(["list_applications"], Tools(handler));
    }

    [Fact]
    public async Task A_tool_error_stops_the_request_as_failed_and_unfinished()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 3,
                ["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "No draft." }), ["isError"] = true },
            }.ToJsonString());
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal(new AuthoringStepResult("failed", "unfinished"), result);
    }
}
