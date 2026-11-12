using System.Net;
using System.Text.Json.Nodes;
using Axis.Agent.Models;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Microsoft.AspNetCore.Hosting;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The working step of the agent host makes the approved change in the request's draft through the write and layer tools of the MCP server, see
/// <c>docs/reference/authoring-requests.md#states</c>. The model is the scripted adapter, and the step reaches the server of <see cref="AuthoringRealm"/> as
/// the author. A call to a tool that is not offered, or one that names another draft or another application, gets a tool error and never reaches the server.
/// A step whose model does not end its turn within 50 model calls, or whose answer stops at the output limit, stops the request as <c>failed</c> with the
/// outcome <c>unfinished</c>, see <c>docs/reference/authoring-requests.md#budgets</c>. The checking step of the same host runs right after a working
/// step, so these tests wait for the working step's row and compare only the tool calls the working step made. The first test also waits for the
/// checking step to start, which shows that the request moved to <c>checking</c>.
/// </summary>
public sealed class AgentWorkingTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string NoDraft = "The application has no draft with this id.";
    private const string Module = "department-budgets";
    private const string ProcessId = "94f1cc25-6a68-435f-9383-36fc43aae860";

    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-agent-working-revisions-").FullName;
    private AuthoringRealm _realm = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_approved_request_gets_a_draft_that_the_model_writes_a_layer_and_a_scenario_in_and_moves_to_checking()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter(
        [
            Plan("Add a finance review step through the layer and a scenario for it."),
            new ModelAnswer(
                [
                    new ModelToolCall("layer-1", "write_layer", AgentPlanningTests.Json(LayerInput())),
                    new ModelToolCall("scenario-1", "write_resource", AgentPlanningTests.Json(ScenarioInput())),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(100, 20)),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server, fixing: false);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not write the plan.");
        using var approved = await StudioPublishTests.SendAsync(client, HttpMethod.Post, $"{Url}/{id}/approve", Host);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await WorkFinishedAsync(id);
        await CheckStartedAsync(id);
        await host.StopAsync(CancellationToken);

        var draft = await ScalarAsync($"SELECT coalesce(draft_id::text, '') FROM axis.authoring_requests WHERE id = '{id}'");
        Assert.True(Guid.TryParse(draft, out _), "The request holds no draft.");
        Assert.Matches(@"^\d+:work:-:160:true$", await StepsAsync(id, "work"));
        Assert.Equal(["list_applications", "list_applications", "create_draft", "write_layer", "write_resource"], server.ToolCalls.Take(5));
        // The steps are plan, work and check in that order, whatever state the check step leaves the request in. The host has no fixing step, so a failed check cannot add a fix step before it stops.
        Assert.Equal("plan,work,check", await ScalarAsync($"SELECT string_agg(kind, ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'"));
        foreach (var tool in new[] { "create_draft", "write_layer", "write_resource" })
        {
            Assert.Equal(
                "1",
                await ScalarAsync($"SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'draftId' = '{draft}' AND details->>'tool' = '{tool}'"));
        }

        // The model left out the draft, so the step filled it in, and the writes were saved.
        var results = script.Calls[2].Messages[^1].Content.OfType<ModelToolResult>().ToList();
        Assert.Equal(["layer-1", "scenario-1"], results.Select(result => result.CallId));
        Assert.All(results, result => Assert.False(result.IsError, result.Content));
        Assert.Contains("customizations/department-budgets/", results[0].Content, StringComparison.Ordinal);
        // The model is offered the five read tools and the six tools that write or check the draft, and no other.
        Assert.Equal(
            [
                "check_expression", "delete_resource", "diff_draft", "list_resources", "read_module_resource", "read_names", "read_reference",
                "read_resource", "validate_draft", "write_layer", "write_resource",
            ],
            script.Calls[1].Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Contains(draft, script.Calls[1].System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_to_a_tool_that_is_not_offered_or_to_another_draft_or_application_gets_a_tool_error_and_never_reaches_the_server()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await WorkingAsync(id);
        var script = new ScriptedModelAdapter(
        [
            new ModelAnswer(
                [
                    new ModelToolCall("create-1", "create_draft", AgentPlanningTests.Json("""{"application":"PurchaseRequests"}""")),
                    new ModelToolCall("run-1", "run_scenarios", AgentPlanningTests.Json("""{"application":"PurchaseRequests"}""")),
                    new ModelToolCall("draft-1", "write_resource", AgentPlanningTests.Json($$$"""{"application":"PurchaseRequests","draft":"{{{Guid.NewGuid():D}}}","content":{}}""")),
                    new ModelToolCall("draft-2", "validate_draft", AgentPlanningTests.Json("""{"application":"PurchaseRequests","draft":7}""")),
                    new ModelToolCall("other-1", "list_resources", AgentPlanningTests.Json("""{"application":"LeaveRequests"}""")),
                    new ModelToolCall("own-1", "read_names", AgentPlanningTests.Json("""{"application":"purchaserequests"}""")),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(10, 5)),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(1, 1)),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WorkFinishedAsync(id);
        await host.StopAsync(CancellationToken);

        var results = script.Calls[1].Messages[^1].Content.OfType<ModelToolResult>().ToList();
        Assert.Equal(["create-1", "run-1", "draft-1", "draft-2", "other-1", "own-1"], results.Select(result => result.CallId));
        Assert.All(results.Take(5), result => Assert.True(result.IsError, result.Content));
        Assert.Equal("The tool is not offered in working.", results[0].Content);
        Assert.Equal("The tool is not offered in working.", results[1].Content);
        Assert.StartsWith("The tool may name only the draft '", results[2].Content, StringComparison.Ordinal);
        Assert.StartsWith("The tool may name only the draft '", results[3].Content, StringComparison.Ordinal);
        Assert.StartsWith("The tool may name only the application '", results[4].Content, StringComparison.Ordinal);
        // The name of the application is matched ignoring letter case, as the server matches it, and the draft is filled in.
        Assert.False(results[5].IsError, results[5].Content);
        // The step's own create_draft is the only one that reaches the server.
        Assert.Equal(["list_applications", "create_draft", "read_names"], server.ToolCalls.Take(3));
        Assert.Equal("1", await ScalarAsync("SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled'"));
    }

    [Fact]
    public async Task A_step_that_runs_again_with_a_stored_draft_makes_no_draft_and_the_model_works_in_the_stored_one()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await WorkingAsync(id);
        var stored = Guid.NewGuid();
        await ScalarAsync($"UPDATE axis.authoring_requests SET draft_id = '{stored}' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter(
        [
            ToolUse("validate-1", "validate_draft", """{"application":"PurchaseRequests"}""", 5, 5),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(1, 1)),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WorkFinishedAsync(id);
        await host.StopAsync(CancellationToken);

        // The server finds no draft with the stored id, which shows that the step filled it in.
        var result = Assert.Single(script.Calls[1].Messages[^1].Content.OfType<ModelToolResult>());
        Assert.True(result.IsError);
        Assert.Equal(NoDraft, result.Content);
        Assert.Equal(["list_applications", "validate_draft"], server.ToolCalls.Take(2));
        // The working step finished and moved the request on. The checking step that follows finds no such draft and stops the request.
        Assert.Matches(@"^\d+:work:-:\d+:true$", await StepsAsync(id, "work"));
        Assert.Contains(stored.ToString("D"), script.Calls[0].System, StringComparison.Ordinal);
        Assert.Equal(stored.ToString("D"), await ScalarAsync($"SELECT draft_id FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_model_that_never_ends_its_turn_within_50_model_calls_stops_the_request_failed_with_the_outcome_unfinished()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await WorkingAsync(id);
        var script = new ScriptedModelAdapter(
            Enumerable.Range(0, 55).Select(number => ToolUse($"call-{number}", "read_names", """{"application":"PurchaseRequests"}""", 1, 1)));
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal(50, script.Calls.Count);
        Assert.Equal("1:work:unfinished:100:true", await StepsAsync(id));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task An_answer_cut_off_at_the_output_limit_stops_the_request_failed_with_the_outcome_unfinished()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await WorkingAsync(id);
        var script = new ScriptedModelAdapter([new ModelAnswer([new ModelText("A change that stops")], ModelStopReason.MaxTokens, new ModelTokens(7, 3))]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:work:unfinished:10:true", await StepsAsync(id));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Single(script.Calls);
    }

    [Fact]
    public async Task A_request_whose_application_the_author_cannot_list_any_more_stops_failed_with_the_outcome_unfinished_and_no_model_call()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await WorkingAsync(id);
        await ScalarAsync($"UPDATE axis.authoring_requests SET application_id = '{Guid.NewGuid()}' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter([Plan("Never used.")]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:work:unfinished:0:true", await StepsAsync(id));
        Assert.Empty(script.Calls);
        Assert.Equal(["list_applications"], server.ToolCalls);
        Assert.Equal("", await ScalarAsync($"SELECT coalesce(draft_id::text, '') FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    public async ValueTask InitializeAsync()
    {
        _realm = new AuthoringRealm(keycloak, database, configure: Configure);
        await _realm.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _realm.DisposeAsync();
        Directory.Delete(_revisions, recursive: true);
    }

    // The draft needs the history of the application's folder, and the layer needs the module the application pins, as McpWriteToolTests sets them.
    private void Configure(IWebHostBuilder builder, string folder)
    {
        builder.UseSetting("Studio:RevisionsPath", _revisions);
        builder.UseSetting("Modules:Path", SampleModules.Path);
        builder.UseSetting("Extensions:Path", SamplePackages.Path);
    }

    // The request is approved without the step before it, as the approval leaves it.
    private async Task WorkingAsync(Guid id) =>
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'A plan.' WHERE id = '{id}' RETURNING 1");

    // The working step's row is finished when it moved the request on in the same transaction, so the checking step may already run.
    private Task WorkFinishedAsync(Guid id) =>
        WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'work' AND finished_at IS NOT NULL") == "1",
            "The host did not finish the working step.");

    // The checking step adds its row when it starts, which shows that the working step moved the request to checking.
    private Task CheckStartedAsync(Guid id) =>
        WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'") == "1",
            "The host did not start the checking step.");

    // The edited process of the module with one step added, as the layer tool takes it.
    internal static string LayerInput(Action<JsonNode>? edit = null)
    {
        var edited = JsonNode.Parse(File.ReadAllText(Path.Combine(SampleModules.Path, Module, "1.2.0", "processes", "budget-approval.json")))!;
        ((JsonArray)edited["steps"]!).Add(JsonNode.Parse(
            """
            {
              "name": "financeReview",
              "type": "task",
              "label": { "textKey": "department-budgets.budgetApproval.review" },
              "assignee": { "role": "approver" },
              "form": "BudgetReview",
              "outcomes": [
                { "name": "approve", "label": { "textKey": "department-budgets.budgetApproval.approve" }, "next": "markApproved" },
                { "name": "reject", "label": { "textKey": "department-budgets.budgetApproval.reject" }, "next": "markRejected" }
              ]
            }
            """));
        edit?.Invoke(edited);
        return new JsonObject { ["application"] = "PurchaseRequests", ["module"] = Module, ["id"] = ProcessId, ["content"] = edited }.ToJsonString();
    }

    // A copy of a scenario of the sample with a new id and name.
    internal static string ScenarioInput()
    {
        var scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests", "scenarios", "approval-below-threshold.json")))!;
        scenario["id"] = Guid.NewGuid().ToString("D");
        scenario["name"] = "FinanceReviewAdded";
        return new JsonObject { ["application"] = "PurchaseRequests", ["content"] = scenario }.ToJsonString();
    }

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> StepsAsync(Guid id, string? kind = null) =>
        ScalarAsync(
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'"
            + (kind is null ? "" : $" AND kind = '{kind}'"));
}
