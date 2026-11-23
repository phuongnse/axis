using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Agent;
using Axis.Agent.Models;
using Axis.Data.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The planning step of the agent host reads the application through the seven read tools of the MCP server and ends with a <c>propose_plan</c> call, see
/// <c>docs/reference/authoring-requests.md#states</c>. The model is the scripted adapter, so no test calls a paid model, and the step reaches the
/// server of <see cref="AuthoringRealm"/> as the author. A call to any other tool, or to another application, gets a tool error and never reaches the server,
/// and so does <c>propose_plan</c>, which the host answers itself. A valid call stores the plan as an object with its title and ends the step.
/// A step that gets no valid call, within 25 model calls or before its turn ends, stops the request as <c>failed</c> with the outcome <c>noPlan</c>.
/// </summary>
public sealed class AgentPlanningTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly AuthoringLoopOptions _options = new(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

    /// <summary>A stored plan of the plan's shape with only a summary, for a test that puts a request in a state that has a plan.</summary>
    internal const string PlanJson = """{"summary":"A plan.","questions":[],"changes":[],"decisions":[],"risks":[],"checks":[],"checklist":[],"details":[]}""";

    private readonly AuthoringRealm _realm = new(keycloak, database);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sent_request_ends_awaitingApproval_with_the_plan_and_its_step_holds_the_tool_calls_and_the_tokens()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter(
        [
            ToolUse("call-1", "list_resources", """{"application":"PurchaseRequests"}""", 100, 20),
            Plan("Add a cost centre field to the purchase request entity.", 150, 40),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("Add a cost centre field to the purchase request entity.", await PlanAsync(id));
        Assert.Equal("1:plan:-:310:true", await StepsAsync(id));
        Assert.Equal("310", await ScalarAsync($"SELECT tokens FROM axis.authoring_requests WHERE id = '{id}'"));
        var messages = await ScalarAsync($"SELECT messages::text FROM axis.authoring_request_steps WHERE request_id = '{id}'");
        Assert.Contains("list_resources", messages, StringComparison.Ordinal);
        Assert.Contains("toolResult", messages, StringComparison.Ordinal);
        Assert.Equal(["list_applications", "list_resources"], server.ToolCalls);
        // The model is offered the five read tools, the two package read tools and propose_plan, and no other.
        Assert.Equal(
            ["list_package_files", "list_resources", "propose_plan", "read_module_resource", "read_names", "read_package_file", "read_reference", "read_resource"],
            script.Calls[0].Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_valid_propose_plan_call_stores_the_plan_as_an_object_with_its_title_and_ends_the_step_with_no_further_model_call()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var input = PlanInput("Purchases get a cost centre.", "Add a cost centre");
        var plan = input["plan"]!.AsObject();
        plan["questions"] = new JsonArray(new JsonObject { ["question"] = "Which team owns it?", ["options"] = new JsonArray("Sales", "Finance"), ["recommended"] = 2 });
        plan["decisions"] = new JsonArray(new JsonObject { ["decision"] = "A list of cost centres", ["reason"] = "A person picks one." });
        plan["risks"] = new JsonArray("Old purchases have no cost centre.");
        plan["checks"] = new JsonArray("A scenario sets a cost centre.");
        plan["details"] = new JsonArray("Edit the purchase entity.");
        var script = new ScriptedModelAdapter([ToolUse("plan-1", "propose_plan", input.ToJsonString(), 5, 5), Answer("Never sent.")]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("Add a cost centre", await ScalarAsync($"SELECT title FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("object", await ScalarAsync($"SELECT jsonb_typeof(plan) FROM axis.authoring_requests WHERE id = '{id}'"));
        var stored = JsonNode.Parse(await ScalarAsync($"SELECT plan::text FROM axis.authoring_requests WHERE id = '{id}'"));
        var expected = JsonNode.Parse(plan.ToJsonString())!;
        Assert.True(JsonNode.DeepEquals(expected, stored), stored!.ToJsonString());
        Assert.Equal("1:plan:-:10:true", await StepsAsync(id));
        Assert.Single(script.Calls);
        // The host answers the call itself, so the server never sees it.
        Assert.Equal(["list_applications"], server.ToolCalls);
        Assert.Contains("The plan is stored.", await ScalarAsync($"SELECT messages::text FROM axis.authoring_request_steps WHERE request_id = '{id}'"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_group_is_stored_as_an_empty_list_so_a_stored_plan_always_has_all_eight_keys()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter([Plan("A plan.")]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("8", await ScalarAsync($"SELECT count(*) FROM jsonb_object_keys((SELECT plan FROM axis.authoring_requests WHERE id = '{id}'))"));
        Assert.Equal("[]", await ScalarAsync($"SELECT (plan->'questions')::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("4", await ScalarAsync($"SELECT jsonb_array_length(plan->'checklist') FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_propose_plan_call_that_breaks_the_shape_gets_a_tool_error_and_a_turn_that_then_ends_is_noPlan_with_no_plan_and_no_title()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter([Plan("A plan.", 4, 1, new string('a', 81)), Answer("Here is the plan in words.", 3, 2)]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:plan:noPlan:10:true", await StepsAsync(id));
        Assert.Equal("true", await ScalarAsync($"SELECT (plan IS NULL AND title IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        var error = Assert.IsType<ModelToolResult>(Assert.Single(script.Calls[1].Messages[^1].Content));
        Assert.True(error.IsError);
        Assert.Contains("80", error.Content, StringComparison.Ordinal);
        Assert.Equal(["list_applications"], server.ToolCalls);
    }

    [Fact]
    public async Task A_model_may_correct_an_invalid_propose_plan_call_and_its_next_valid_call_stores_the_plan()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter([Plan("A plan.", title: ""), Plan("The corrected plan.", title: "Add a cost centre")]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("The corrected plan.", await PlanAsync(id));
        Assert.Equal("Add a cost centre", await ScalarAsync($"SELECT title FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal(2, script.Calls.Count);
    }

    [Fact]
    public async Task The_read_route_answers_title_null_while_the_request_is_planning_and_the_stored_title_and_the_plan_object_after_the_plan()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        using (var planning = await StudioPublishTests.SendAsync(client, HttpMethod.Get, $"{Url}/{id}", Host))
        {
            Assert.Equal(HttpStatusCode.OK, planning.StatusCode);
            using var body = await StudioPublishTests.ReadAsync(planning);
            Assert.Equal("planning", body.RootElement.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("title").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("plan").ValueKind);
        }

        var script = new ScriptedModelAdapter([Plan("Purchases get a cost centre.", title: "Add a cost centre")]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        using var planned = await StudioPublishTests.SendAsync(client, HttpMethod.Get, $"{Url}/{id}", Host);
        using var read = await StudioPublishTests.ReadAsync(planned);
        Assert.Equal("Add a cost centre", read.RootElement.GetProperty("title").GetString());
        var plan = read.RootElement.GetProperty("plan");
        Assert.Equal(JsonValueKind.Object, plan.ValueKind);
        Assert.Equal("Purchases get a cost centre.", plan.GetProperty("summary").GetString());
        Assert.Equal("Change: Purchases get a cost centre.", plan.GetProperty("changes")[0].GetString());
        Assert.Equal(PlanShape.ChecklistItems, plan.GetProperty("checklist").EnumerateArray().Select(entry => entry.GetProperty("item").GetString()));
        Assert.Equal(0, plan.GetProperty("questions").GetArrayLength());

        using var list = await StudioPublishTests.SendAsync(client, HttpMethod.Get, Url, Host);
        using var listed = await StudioPublishTests.ReadAsync(list);
        Assert.Equal("Add a cost centre", listed.RootElement.GetProperty("items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task After_a_change_of_plan_the_next_step_sees_the_old_plan_and_the_comment_and_its_plan_replaces_the_old_one()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter([Plan("The first plan."), Plan("The second plan.")]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not write the first plan.");
        using var changed = await StudioPublishTests.SendAsync(
            client, HttpMethod.Post, $"{Url}/{id}/change-plan", Host, """{ "comment": "Also add it to the form." }""");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await WaitUntilAsync(
            async () => await StateAsync(id) == "awaitingApproval" && await PlanAsync(id) == "The second plan.",
            "The host did not replace the plan.");
        await host.StopAsync(CancellationToken);

        var first = Assert.IsType<ModelText>(Assert.Single(script.Calls[0].Messages[0].Content)).Text;
        Assert.DoesNotContain("The first plan.", first, StringComparison.Ordinal);
        var second = Assert.IsType<ModelText>(Assert.Single(script.Calls[1].Messages[0].Content)).Text;
        Assert.Contains("The first plan.", second, StringComparison.Ordinal);
        Assert.Contains("Also add it to the form.", second, StringComparison.Ordinal);
        Assert.Contains("Add a cost centre to every purchase request.", second, StringComparison.Ordinal);
        Assert.Equal("1:plan:-:0:true,3:plan:-:0:true", await StepsAsync(id, kind: "plan"));
    }

    [Fact]
    public async Task A_call_to_a_write_tool_and_a_call_naming_another_application_get_a_tool_error_and_never_reach_the_server()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter(
        [
            new ModelAnswer(
                [
                    new ModelToolCall("write-1", "write_resource", Json("""{"application":"PurchaseRequests","draft":"d","content":{}}""")),
                    new ModelToolCall("package-1", "write_package_file", Json("""{"application":"PurchaseRequests","draft":"d","package":"tools","path":"adapter.py","content":""}""")),
                    new ModelToolCall("test-1", "test_package", Json("""{"application":"PurchaseRequests","draft":"d","package":"tools"}""")),
                    new ModelToolCall("other-1", "list_resources", Json("""{"application":"LeaveRequests"}""")),
                    new ModelToolCall("own-1", "read_names", Json("""{"application":"purchaserequests"}""")),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(10, 5)),
            Plan("A plan."),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        var results = script.Calls[1].Messages[^1].Content.OfType<ModelToolResult>().ToList();
        Assert.Equal(["write-1", "package-1", "test-1", "other-1", "own-1"], results.Select(result => result.CallId));
        Assert.All(results.Take(3), result => Assert.Equal("The tool is not offered in planning.", result.Content));
        Assert.All(results.Take(4), result => Assert.True(result.IsError));
        // The name of the application is matched ignoring letter case, as the server matches it.
        Assert.False(results[4].IsError);
        Assert.Equal(["list_applications", "read_names"], server.ToolCalls);
        Assert.Equal("0", await ScalarAsync("SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled'"));
    }

    [Fact]
    public async Task A_script_that_never_ends_its_turn_within_25_model_calls_stops_the_request_failed_with_the_outcome_noPlan()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter(
            Enumerable.Range(0, 30).Select(number => ToolUse($"call-{number}", "read_names", """{"application":"PurchaseRequests"}""", 1, 1)));
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal(25, script.Calls.Count);
        Assert.Equal("1:plan:noPlan:50:true", await StepsAsync(id));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("", await PlanAsync(id));
    }

    [Theory]
    [InlineData(ModelStopReason.MaxTokens, "A plan that stops")]
    [InlineData(ModelStopReason.EndTurn, "  ")]
    public async Task An_answer_cut_off_at_the_output_limit_or_with_empty_text_is_no_plan(ModelStopReason reason, string text)
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter([new ModelAnswer([new ModelText(text)], reason, new ModelTokens(7, 3))]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:plan:noPlan:10:true", await StepsAsync(id));
        Assert.Equal("", await PlanAsync(id));
        Assert.Single(script.Calls);
    }

    [Fact]
    public async Task A_request_whose_application_the_author_cannot_list_any_more_stops_failed_with_the_outcome_noPlan_and_no_model_call()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ScalarAsync($"UPDATE axis.authoring_requests SET application_id = '{Guid.NewGuid()}' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter([Plan("A plan.")]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:plan:noPlan:0:true", await StepsAsync(id));
        Assert.Empty(script.Calls);
    }

    public async ValueTask InitializeAsync() => await _realm.InitializeAsync();

    public async ValueTask DisposeAsync() => await _realm.DisposeAsync();

    internal static ModelAnswer ToolUse(string callId, string tool, string input, int inputTokens, int outputTokens) =>
        new([new ModelToolCall(callId, tool, Json(input))], ModelStopReason.ToolUse, new ModelTokens(inputTokens, outputTokens));

    // The answer that ends a planning step: one propose_plan call with the plan's shape, its only change "Change: <summary>" and Axis's checklist.
    internal static ModelAnswer Plan(string summary, int inputTokens = 0, int outputTokens = 0, string title = "Add a cost centre") =>
        ToolUse("plan-1", "propose_plan", PlanInput(summary, title).ToJsonString(), inputTokens, outputTokens);

    internal static JsonObject PlanInput(string summary, string title) => new()
    {
        ["application"] = "PurchaseRequests",
        ["title"] = title,
        ["plan"] = new JsonObject
        {
            ["summary"] = summary,
            ["changes"] = new JsonArray($"Change: {summary}"),
            ["checklist"] = new JsonArray([.. PlanShape.ChecklistItems.Select(item => (JsonNode)new JsonObject { ["item"] = item, ["how"] = "The plan does." })]),
        },
    };

    // The answer of a step that ends its turn with text, such as the review, rather than with a tool call.
    internal static ModelAnswer Answer(string text, int inputTokens = 0, int outputTokens = 0) =>
        new([new ModelText(text)], ModelStopReason.EndTurn, new ModelTokens(inputTokens, outputTokens));

    internal static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static async Task<Guid> SendAsync(HttpClient client)
    {
        using var accepted = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var sent = await StudioPublishTests.ReadAsync(accepted);
        return Guid.Parse(sent.RootElement.GetProperty("id").GetString()!);
    }

    // The client reaches the server of the factory through a handler that records the tools it calls. A test that stops a request in fixing and asserts
    // its exact steps leaves out the fix step, so the host does not claim the request again. A test may leave out other steps by the name of their class.
    internal static WebApplication BuildAgent(
        AuthoringRealm realm,
        ScriptedModelAdapter script,
        RecordingHandler server,
        IReadOnlyDictionary<string, string?>? settings = null,
        bool fixing = true,
        IReadOnlyCollection<string>? without = null)
    {
        List<string> left = [.. without ?? []];
        if (!fixing)
        {
            left.Add("FixingStep");
        }

        var values = realm.AgentSettings();
        foreach (var (name, value) in settings ?? new Dictionary<string, string?>())
        {
            values[name] = value;
        }

        return AgentTestHost.Build(
            aiOn: true,
            values,
            new LogCollector(),
            services =>
            {
                services.AddSingleton(_options);
                services.AddSingleton(script);
                services.AddHttpClient(AuthorMcpClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
                foreach (var step in left)
                {
                    services.Remove(services.Single(service => service.ServiceType == typeof(IAuthoringStep) && service.ImplementationType?.Name == step));
                }
            });
    }

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> PlanAsync(Guid id) => ScalarAsync($"SELECT coalesce(plan->>'summary', '') FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> StepsAsync(Guid id, string? kind = null) =>
        ScalarAsync(
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'"
            + (kind is null ? "" : $" AND kind = '{kind}'"));

    // Passes every call to the server and records the name of each tools/call.
    internal sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly ConcurrentQueue<string> _toolCalls = new();

        public IReadOnlyList<string> ToolCalls => [.. _toolCalls];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                if (body.RootElement.TryGetProperty("method", out var method) && method.GetString() == "tools/call")
                {
                    _toolCalls.Enqueue(body.RootElement.GetProperty("params").GetProperty("name").GetString()!);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
