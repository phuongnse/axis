using System.Net;
using System.Text.Json.Nodes;
using Axis.Tenancy.Realms;

namespace Axis.Agent.Tests;

/// <summary>
/// The checking step compiles the draft, tests each package the draft changed, runs every scenario on it when it compiles and moves the request to
/// <c>reviewing</c> or <c>fixing</c>, see <c>docs/reference/authoring-requests.md#states</c>. A run that does not end within the allowed polls stops
/// the request, see <c>docs/reference/authoring-requests.md#budgets</c>.
/// </summary>
public sealed class CheckingStepTests
{
    private static readonly Guid _applicationId = Guid.Parse("0f0c3b6e-3d1f-4d5e-9a57-6c1f4b0f2a11");
    private static readonly Guid _draftId = Guid.Parse("7d1a5c52-0c1e-4b57-8c07-9f0d9d1e5b33");
    private const string RunId = "0192c5a0-0000-7000-8000-000000000001";
    private const string PackageRunId = "0192c5a0-0000-7000-8000-000000000002";
    private const string NoChanges = """{"start":"s","tip":"t","resources":[],"cursor":null}""";

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

    // The session opening.
    private static RecordingHandler Session(RecordingHandler handler) =>
        handler
            .Answer(HttpStatusCode.OK, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["result"] = new JsonObject { ["protocolVersion"] = McpProtocol.Version, ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() } },
            }.ToJsonString(), response => response.Headers.Add("Mcp-Session-Id", "session-1"))
            .Answer(HttpStatusCode.Accepted);

    // The session opening, then list_applications, which the step reads first.
    private static RecordingHandler Opened(RecordingHandler handler) =>
        Session(handler)
            .Answer(HttpStatusCode.OK, Message(2, $$"""{"items":[{"id":"{{_applicationId}}","name":"PurchaseRequests"}]}"""));

    // An output cut in two pages of { part, cursor } at the index <cut>, the way the server pages an output over 1 MiB, with ids from <firstId>.
    private static RecordingHandler Paged(RecordingHandler handler, int firstId, string output, int cut) =>
        handler
            .Answer(HttpStatusCode.OK, PageMessage(firstId, output[..cut], "c1"))
            .Answer(HttpStatusCode.OK, PageMessage(firstId + 1, output[cut..], null));

    private static string PageMessage(int id, string part, string? cursor) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "page" }),
                ["structuredContent"] = new JsonObject { ["part"] = part, ["cursor"] = cursor },
                ["isError"] = false,
            },
        }.ToJsonString();

    private static AuthoringStepContext NewContext(RecordingHandler handler, Guid? draft = null)
    {
        var client = new AuthorMcpClient(new HttpClient(handler), "a.example.test", _ => Task.FromResult("token"));
        var request = new AuthoringRequestToWork(Guid.NewGuid(), "checking", DateTimeOffset.UtcNow, _applicationId, "Add a review", "A plan", null, [], draft ?? _draftId, null, 0, null);
        return new AuthoringStepContext(
            "tenant",
            request,
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult("token"),
            (_, _) => Task.CompletedTask,
            client,
            long.MaxValue);
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
        Assert.Equal("notRun", checks["packages"]!["outcome"]!.GetValue<string>());
        Assert.Empty(checks["packages"]!["items"]!.AsArray());
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
            .Answer(HttpStatusCode.OK, Message(4, NoChanges))
            .Answer(HttpStatusCode.OK, Message(5, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(6, $$"""{"request":"{{RunId}}","status":"running","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(7, $$"""{"request":"{{RunId}}","status":"succeeded","diagnostics":[],"scenarios":[{"name":"A","outcome":"passed"}]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        Assert.Equal("passed", result.Outcome);
        var checks = JsonNode.Parse(result.Checks!)!;
        Assert.Equal("passed", checks["compile"]!["outcome"]!.GetValue<string>());
        Assert.Equal("warning", checks["compile"]!["diagnostics"]![0]!["severity"]!.GetValue<string>());
        Assert.Equal("notRun", checks["packages"]!["outcome"]!.GetValue<string>());
        Assert.Equal("passed", checks["scenarios"]!["outcome"]!.GetValue<string>());
        Assert.Equal(RunId, checks["scenarios"]!["request"]!.GetValue<string>());
        Assert.Equal("A", checks["scenarios"]!["items"]![0]!["name"]!.GetValue<string>());
        var starts = handler.Requests.Select(request => JsonNode.Parse(request.Body)!).Where(message => message["params"]?["name"]?.GetValue<string>() == "run_scenarios").ToList();
        Assert.Null(starts[0]["params"]!["arguments"]!["request"]);
        Assert.Null(starts[0]["params"]!["arguments"]!["names"]);
        Assert.Equal(RunId, starts[1]["params"]!["arguments"]!["request"]!.GetValue<string>());
        Assert.Equal(["list_applications", "validate_draft", "diff_draft", "run_scenarios", "run_scenarios", "run_scenarios"], Tools(handler));
    }

    [Fact]
    public async Task A_failed_run_keeps_its_items_and_moves_the_request_to_fixing()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, Message(3, """{"diagnostics":[]}"""))
            .Answer(HttpStatusCode.OK, Message(4, NoChanges))
            .Answer(HttpStatusCode.OK, Message(5, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(6, $$"""{"request":"{{RunId}}","status":"failed","diagnostics":[],"scenarios":[{"name":"m/A","outcome":"failed","step":"expectInstance"}]}"""));
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
            .Answer(HttpStatusCode.OK, Message(4, NoChanges))
            .Answer(HttpStatusCode.OK, Message(5, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(6, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""))
            .Answer(HttpStatusCode.OK, Message(7, $$"""{"request":"{{RunId}}","status":"queued","diagnostics":[],"scenarios":[]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("failed", result.NextState);
        Assert.Equal("unfinished", result.Outcome);
        Assert.Null(result.Checks);
        Assert.Equal(["list_applications", "validate_draft", "diff_draft", "run_scenarios", "run_scenarios", "run_scenarios"], Tools(handler));
    }

    [Fact]
    public async Task A_request_without_a_draft_stops_as_failed_and_unfinished_before_any_check()
    {
        var handler = Opened(new RecordingHandler());
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);
        var request = new AuthoringRequestToWork(Guid.NewGuid(), "checking", DateTimeOffset.UtcNow, _applicationId, "Add a review", "A plan", null, [], null, null, 0, null);
        var client = new AuthorMcpClient(new HttpClient(handler), "a.example.test", _ => Task.FromResult("token"));
        var context = new AuthoringStepContext("tenant", request, (_, _) => Task.CompletedTask, _ => Task.FromResult("token"), (_, _) => Task.CompletedTask, client, long.MaxValue);

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

    // The answers after list_applications and validate_draft, numbered from 4 as the client sends them.
    private static RecordingHandler Then(RecordingHandler handler, params string[] answers)
    {
        for (var i = 0; i < answers.Length; i++)
        {
            handler.Answer(HttpStatusCode.OK, Message(4 + i, answers[i]));
        }

        return handler;
    }

    private static RecordingHandler Compiled(RecordingHandler handler) =>
        Opened(handler).Answer(HttpStatusCode.OK, Message(3, """{"diagnostics":[]}"""));

    private static string Diff(string cursor, params string[] files) =>
        $$"""{"start":"s","tip":"t","resources":[{{string.Join(",", files.Select(file => $$"""{"file":"{{file}}","change":"added"}"""))}}],"cursor":{{cursor}}}""";

    private static string PackageRun(string status, string tests = "[]") =>
        $$"""{"request":"{{PackageRunId}}","status":"{{status}}","diagnostics":[],"tests":{{tests}}}""";

    private static string ScenarioRun(string status) =>
        $$"""{"request":"{{RunId}}","status":"{{status}}","diagnostics":[],"scenarios":[]}""";

    private static IReadOnlyList<JsonNode> Calls(RecordingHandler handler, string tool) =>
    [
        .. handler.Requests
            .Select(request => JsonNode.Parse(request.Body)!)
            .Where(message => message["params"]?["name"]?.GetValue<string>() == tool),
    ];

    [Fact]
    public async Task A_changed_package_that_the_draft_holds_is_tested_after_the_compile_and_before_the_scenarios_and_stored_with_its_request_and_tests()
    {
        var handler = Then(
            Compiled(new RecordingHandler()),
            Diff("null", "packages/tools/adapter.py", "packages/tools/axis-package.json", "resources/a.json", "packages/loose.txt"),
            """{"items":[{"package":"tools","path":"adapter.py","version":"v1"},{"package":"other","path":"adapter.py","version":"v1"}]}""",
            PackageRun("queued"),
            PackageRun("succeeded", """[{"name":"test_tools.A.test_ok","outcome":"passed","message":""}]"""),
            ScenarioRun("queued"),
            ScenarioRun("succeeded"));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        Assert.Equal("passed", result.Outcome);
        var packages = JsonNode.Parse(result.Checks!)!["packages"]!;
        Assert.Equal("passed", packages["outcome"]!.GetValue<string>());
        var item = Assert.Single(packages["items"]!.AsArray())!;
        Assert.Equal("tools", item["package"]!.GetValue<string>());
        Assert.Equal(PackageRunId, item["request"]!.GetValue<string>());
        Assert.Equal("succeeded", item["status"]!.GetValue<string>());
        Assert.Equal("test_tools.A.test_ok", item["tests"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(
            ["list_applications", "validate_draft", "diff_draft", "list_package_files", "test_package", "test_package", "run_scenarios", "run_scenarios"],
            Tools(handler));
        var tests = Calls(handler, "test_package");
        Assert.Equal("tools", tests[0]["params"]!["arguments"]!["package"]!.GetValue<string>());
        Assert.Equal(_draftId.ToString("D"), tests[0]["params"]!["arguments"]!["draft"]!.GetValue<string>());
        Assert.Null(tests[0]["params"]!["arguments"]!["request"]);
        Assert.Equal(PackageRunId, tests[1]["params"]!["arguments"]!["request"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_failed_package_test_with_passing_scenarios_moves_the_request_to_fixing()
    {
        var handler = Then(
            Compiled(new RecordingHandler()),
            Diff("null", "packages/tools/adapter.py"),
            """{"items":[{"package":"tools","path":"adapter.py","version":"v1"}]}""",
            PackageRun("queued"),
            PackageRun("failed", """[{"name":"test_tools.A.test_bad","outcome":"failed","message":"boom"}]"""),
            ScenarioRun("queued"),
            ScenarioRun("succeeded"));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("fixing", result.NextState);
        Assert.Equal("failed", result.Outcome);
        var checks = JsonNode.Parse(result.Checks!)!;
        Assert.Equal("failed", checks["packages"]!["outcome"]!.GetValue<string>());
        Assert.Equal("failed", checks["packages"]!["items"]![0]!["tests"]![0]!["outcome"]!.GetValue<string>());
        Assert.Equal("passed", checks["scenarios"]!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_package_that_the_draft_changed_but_no_longer_holds_is_not_tested()
    {
        var handler = Then(
            Compiled(new RecordingHandler()),
            Diff("null", "packages/gone/adapter.py"),
            """{"items":[]}""",
            ScenarioRun("queued"),
            ScenarioRun("succeeded"));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        var packages = JsonNode.Parse(result.Checks!)!["packages"]!;
        Assert.Equal("notRun", packages["outcome"]!.GetValue<string>());
        Assert.Empty(packages["items"]!.AsArray());
        Assert.DoesNotContain("test_package", Tools(handler));
    }

    [Fact]
    public async Task A_diff_with_no_package_file_tests_no_package_and_lists_no_package_files()
    {
        var handler = Then(Compiled(new RecordingHandler()), Diff("null", "resources/a.json"), ScenarioRun("queued"), ScenarioRun("succeeded"));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        Assert.Equal("notRun", JsonNode.Parse(result.Checks!)!["packages"]!["outcome"]!.GetValue<string>());
        Assert.DoesNotContain("list_package_files", Tools(handler));
        Assert.DoesNotContain("test_package", Tools(handler));
    }

    [Fact]
    public async Task A_diff_of_two_pages_is_read_to_its_last_page_before_the_packages_are_found()
    {
        var handler = Then(
            Compiled(new RecordingHandler()),
            Diff("\"c1\"", "resources/a.json"),
            Diff("null", "packages/tools/adapter.py"),
            """{"items":[{"package":"tools","path":"adapter.py","version":"v1"}]}""",
            PackageRun("queued"),
            PackageRun("succeeded"),
            ScenarioRun("queued"),
            ScenarioRun("succeeded"));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        var diffs = Calls(handler, "diff_draft");
        Assert.Equal(2, diffs.Count);
        Assert.Null(diffs[0]["params"]!["arguments"]!["cursor"]);
        Assert.Equal("c1", diffs[1]["params"]!["arguments"]!["cursor"]!.GetValue<string>());
        Assert.Equal("tools", JsonNode.Parse(result.Checks!)!["packages"]!["items"]![0]!["package"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_test_package_tool_error_stops_the_request_as_failed_and_unfinished()
    {
        var handler = Then(
            Compiled(new RecordingHandler()),
            Diff("null", "packages/tools/adapter.py"),
            """{"items":[{"package":"tools","path":"adapter.py","version":"v1"}]}""")
            .Answer(HttpStatusCode.OK, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 7,
                ["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "You do not hold the developer role." }), ["isError"] = true },
            }.ToJsonString());
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal(new AuthoringStepResult("failed", "unfinished"), result);
        Assert.DoesNotContain("run_scenarios", Tools(handler));
    }

    [Fact]
    public async Task A_validate_draft_answer_in_two_pages_with_an_error_on_the_second_moves_the_request_to_fixing()
    {
        const string Output = """{"diagnostics":[{"code":"AX2","severity":"warning","message":"odd","file":"a.json","path":"","resourceId":null},{"code":"AX1","severity":"error","message":"broken","file":"a.json","path":"","resourceId":null}]}""";
        var handler = Paged(Opened(new RecordingHandler()), 3, Output, Output.IndexOf("{\"code\":\"AX1\"", StringComparison.Ordinal));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("fixing", result.NextState);
        Assert.Equal("failed", result.Outcome);
        var compile = JsonNode.Parse(result.Checks!)!["compile"]!;
        Assert.Equal("failed", compile["outcome"]!.GetValue<string>());
        Assert.Equal(["AX2", "AX1"], compile["diagnostics"]!.AsArray().Select(diagnostic => diagnostic!["code"]!.GetValue<string>()));
        Assert.Equal(["list_applications", "validate_draft", "validate_draft"], Tools(handler));
        var calls = Calls(handler, "validate_draft");
        Assert.Null(calls[0]["params"]!["arguments"]!["cursor"]);
        Assert.Equal("c1", calls[1]["params"]!["arguments"]!["cursor"]!.GetValue<string>());
        Assert.Equal("PurchaseRequests", calls[1]["params"]!["arguments"]!["application"]!.GetValue<string>());
        Assert.Equal(_draftId.ToString("D"), calls[1]["params"]!["arguments"]!["draft"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_list_package_files_answer_in_two_pages_tests_a_package_listed_only_on_the_second()
    {
        const string Output = """{"items":[{"package":"other","path":"adapter.py","version":"v1"},{"package":"tools","path":"adapter.py","version":"v1"}]}""";
        var handler = Then(Compiled(new RecordingHandler()), Diff("null", "packages/tools/adapter.py"));
        Paged(handler, 5, Output, Output.IndexOf("{\"package\":\"tools\"", StringComparison.Ordinal));
        handler
            .Answer(HttpStatusCode.OK, Message(7, PackageRun("queued")))
            .Answer(HttpStatusCode.OK, Message(8, PackageRun("succeeded")))
            .Answer(HttpStatusCode.OK, Message(9, ScenarioRun("queued")))
            .Answer(HttpStatusCode.OK, Message(10, ScenarioRun("succeeded")));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 5), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("reviewing", result.NextState);
        Assert.Equal("tools", Calls(handler, "test_package")[0]["params"]!["arguments"]!["package"]!.GetValue<string>());
        Assert.Equal("c1", Calls(handler, "list_package_files")[1]["params"]!["arguments"]!["cursor"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_list_applications_answer_in_two_pages_still_gives_the_name_of_the_application()
    {
        var output = $$"""{"items":[{"id":"{{Guid.NewGuid()}}","name":"Other"},{"id":"{{_applicationId}}","name":"PurchaseRequests"}]}""";
        var handler = Paged(Session(new RecordingHandler()), 2, output, output.IndexOf("PurchaseRequests", StringComparison.Ordinal) - 5)
            .Answer(HttpStatusCode.OK, Message(4, """{"diagnostics":[{"code":"AX1","severity":"error","message":"broken","file":"a.json","path":"","resourceId":null}]}"""));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal("fixing", result.NextState);
        Assert.Equal(["list_applications", "list_applications", "validate_draft"], Tools(handler));
        var calls = Calls(handler, "list_applications");
        Assert.Equal("""{"cursor":"c1"}""", calls[1]["params"]!["arguments"]!.ToJsonString());
        Assert.Equal("PurchaseRequests", Calls(handler, "validate_draft")[0]["params"]!["arguments"]!["application"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_page_that_is_a_tool_error_stops_the_request_as_failed_and_unfinished()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, PageMessage(3, """{"diagnostics":[""", "c1"))
            .Answer(HttpStatusCode.OK, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 4,
                ["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "The cursor is not one this output gave." }), ["isError"] = true },
            }.ToJsonString());
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal(new AuthoringStepResult("failed", "unfinished"), result);
        Assert.Equal(["list_applications", "validate_draft", "validate_draft"], Tools(handler));
    }

    [Fact]
    public async Task Pages_that_join_into_something_other_than_JSON_stop_the_request_as_failed_and_unfinished()
    {
        var handler = Opened(new RecordingHandler())
            .Answer(HttpStatusCode.OK, PageMessage(3, """{"diagnostics":[""", "c1"))
            .Answer(HttpStatusCode.OK, PageMessage(4, "not json", null));
        var step = new CheckingStep(new CheckingOptions(TimeSpan.Zero, 2), TimeProvider.System);

        var result = await step.RunAsync(NewContext(handler), CancellationToken);

        Assert.Equal(new AuthoringStepResult("failed", "unfinished"), result);
    }
}
