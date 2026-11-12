using System.Net;
using System.Text.Json;
using Axis.Agent.Models;
using Axis.Data.Storage;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The review step of the agent host reviews the request's draft in a new model session, see <c>docs/reference/authoring-requests.md#states</c>. The model is the
/// scripted adapter, and the step reaches the server of <see cref="AuthoringRealm"/> as the author. An answer whose first line is <c>PASSED</c> makes the request
/// <c>ready</c>, and any other text moves it to <c>fixing</c> with the text as the findings, which the read route answers in the <c>checks</c> of the
/// <c>review</c> step, see <c>docs/reference/authoring-request-storage.md#authoring-requests</c>. The review is offered only the five read tools and
/// <c>diff_draft</c>, and a call to any other tool gets a tool error and never reaches the server.
/// </summary>
public sealed class AgentReviewingTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Findings = "The scenario for the finance review is missing.";
    private const string LatestChecks = """{"compile":{"outcome":"passed"},"scenarios":{"outcome":"passed"}}""";

    private readonly AuthoringRealm _realm = new(keycloak, database);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_review_that_answers_findings_moves_the_request_to_fixing_and_the_read_route_answers_them_in_the_checks_of_the_review_step()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ReviewingAsync(id);
        var script = new ScriptedModelAdapter([Plan(Findings, 40, 10)]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "fixing", "The host did not move the request to fixing.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:check:passed:0:true,2:review:findings:50:true", await StepsAsync(id));
        using var read = await StudioPublishTests.SendAsync(client, HttpMethod.Get, $"{Url}/{id}", Host);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var request = await StudioPublishTests.ReadAsync(read);
        var steps = request.RootElement.GetProperty("steps").EnumerateArray().ToList();
        var review = steps[^1];
        Assert.Equal("review", review.GetProperty("kind").GetString());
        Assert.Equal("findings", review.GetProperty("outcome").GetString());
        var checks = review.GetProperty("checks").GetProperty("review");
        Assert.Equal("findings", checks.GetProperty("outcome").GetString());
        Assert.Equal(Findings, checks.GetProperty("findings").GetString());

        // A new conversation: the request, the plan and the latest check results, and only the five read tools and diff_draft.
        var call = Assert.Single(script.Calls);
        var first = Assert.IsType<ModelText>(Assert.Single(call.Messages).Content[0]);
        Assert.Contains("A plan.", first.Text, StringComparison.Ordinal);
        Assert.Contains(LatestChecks.Replace(" ", "", StringComparison.Ordinal), first.Text.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(
            ["diff_draft", "list_resources", "read_module_resource", "read_names", "read_reference", "read_resource"],
            call.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["list_applications"], server.ToolCalls);
    }

    [Fact]
    public async Task A_call_to_a_tool_that_writes_gets_a_tool_error_and_never_reaches_the_server_and_a_PASSED_answer_makes_the_request_ready()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ReviewingAsync(id);
        var script = new ScriptedModelAdapter(
        [
            ToolUse("write-1", "write_resource", """{"application":"PurchaseRequests","content":{}}""", 10, 5),
            Plan("PASSED\nThe draft does the request.", 20, 5),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(_realm, script, server);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "ready", "The host did not make the request ready.");
        await host.StopAsync(CancellationToken);

        var result = Assert.Single(script.Calls[1].Messages[^1].Content.OfType<ModelToolResult>());
        Assert.Equal("write-1", result.CallId);
        Assert.True(result.IsError);
        Assert.Equal("The tool is not offered in reviewing.", result.Content);
        Assert.Equal(["list_applications"], server.ToolCalls);
        Assert.Equal("0", await ScalarAsync("SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled'"));
        Assert.Equal("1:check:passed:0:true,2:review:passed:40:true", await StepsAsync(id));
        Assert.Equal("passed", await ScalarAsync($"SELECT checks->'review'->>'outcome' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'review'"));
        Assert.Equal("true", await ScalarAsync($"SELECT (checks->'review'->'findings' = 'null'::jsonb)::text FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'review'"));
    }

    public async ValueTask InitializeAsync() => await _realm.InitializeAsync();

    public async ValueTask DisposeAsync() => await _realm.DisposeAsync();

    // The request is as the checking step leaves it: in reviewing, with a plan, a draft and the results of a finished check step.
    private async Task ReviewingAsync(Guid id)
    {
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'reviewing', plan = 'A plan.', draft_id = '{Guid.NewGuid()}' WHERE id = '{id}' RETURNING 1");
        await ScalarAsync(
            $"INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, outcome, checks, finished_at) VALUES ('{Guid.NewGuid()}', '{id}', 1, 'check', 'passed', '{LatestChecks}'::jsonb, now()) RETURNING 1");
    }

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> StepsAsync(Guid id) =>
        ScalarAsync(
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'");
}
