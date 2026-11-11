using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Axis.Agent;
using Axis.Agent.Models;
using Axis.Data.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The planning step of the agent host writes a plan from the five read tools of the MCP server, see
/// <c>docs/reference/authoring-requests.md#states</c>. The model is the scripted adapter, so no test calls a paid model, and the step reaches the
/// server of <see cref="AuthoringRealm"/> as the author. A call to any other tool, or to another application, gets a tool error and never reaches the server.
/// A step that gets no plan within 25 model calls stops the request as <c>failed</c> with the outcome <c>noPlan</c>.
/// </summary>
public sealed class AgentPlanningTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly AuthoringLoopOptions _options = new(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

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
        await using var host = BuildAgent(script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("Add a cost centre field to the purchase request entity.", await ScalarAsync($"SELECT plan FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("1:plan:-:310:true", await StepsAsync(id));
        Assert.Equal("310", await ScalarAsync($"SELECT tokens FROM axis.authoring_requests WHERE id = '{id}'"));
        var messages = await ScalarAsync($"SELECT messages::text FROM axis.authoring_request_steps WHERE request_id = '{id}'");
        Assert.Contains("list_resources", messages, StringComparison.Ordinal);
        Assert.Contains("toolResult", messages, StringComparison.Ordinal);
        Assert.Equal(["list_applications", "list_resources"], server.ToolCalls);
        // The model is offered the five read tools and no other.
        Assert.Equal(
            ["list_resources", "read_module_resource", "read_names", "read_reference", "read_resource"],
            script.Calls[0].Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task After_a_change_of_plan_the_next_step_sees_the_old_plan_and_the_comment_and_its_plan_replaces_the_old_one()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter([Plan("The first plan."), Plan("The second plan.")]);
        await using var host = BuildAgent(script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
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
                    new ModelToolCall("other-1", "list_resources", Json("""{"application":"LeaveRequests"}""")),
                    new ModelToolCall("own-1", "read_names", Json("""{"application":"purchaserequests"}""")),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(10, 5)),
            Plan("A plan."),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        var results = script.Calls[1].Messages[^1].Content.OfType<ModelToolResult>().ToList();
        Assert.Equal(["write-1", "other-1", "own-1"], results.Select(result => result.CallId));
        Assert.True(results[0].IsError);
        Assert.True(results[1].IsError);
        // The name of the application is matched ignoring letter case, as the server matches it.
        Assert.False(results[2].IsError);
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
        await using var host = BuildAgent(script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
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
        await using var host = BuildAgent(script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
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
        await using var host = BuildAgent(script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:plan:noPlan:0:true", await StepsAsync(id));
        Assert.Empty(script.Calls);
    }

    public async ValueTask InitializeAsync() => await _realm.InitializeAsync();

    public async ValueTask DisposeAsync() => await _realm.DisposeAsync();

    private static ModelAnswer ToolUse(string callId, string tool, string input, int inputTokens, int outputTokens) =>
        new([new ModelToolCall(callId, tool, Json(input))], ModelStopReason.ToolUse, new ModelTokens(inputTokens, outputTokens));

    private static ModelAnswer Plan(string text, int inputTokens = 0, int outputTokens = 0) =>
        new([new ModelText(text)], ModelStopReason.EndTurn, new ModelTokens(inputTokens, outputTokens));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<Guid> SendAsync(HttpClient client)
    {
        using var accepted = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var sent = await StudioPublishTests.ReadAsync(accepted);
        return Guid.Parse(sent.RootElement.GetProperty("id").GetString()!);
    }

    // The client reaches the server of the factory through a handler that records the tools it calls.
    private WebApplication BuildAgent(ScriptedModelAdapter script, RecordingHandler server) =>
        AgentTestHost.Build(
            aiOn: true,
            _realm.AgentSettings(),
            new LogCollector(),
            services =>
            {
                services.AddSingleton(_options);
                services.AddSingleton(script);
                services.AddHttpClient(AuthorMcpClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
            });

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> PlanAsync(Guid id) => ScalarAsync($"SELECT coalesce(plan, '') FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> StepsAsync(Guid id, string? kind = null) =>
        ScalarAsync(
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'"
            + (kind is null ? "" : $" AND kind = '{kind}'"));

    // Passes every call to the server and records the name of each tools/call.
    private sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
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
