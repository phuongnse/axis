using System.Net;
using Axis.Agent.Models;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using static Axis.Integration.Tests.AgentCheckingTests;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AgentWorkingTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The fixing step of the agent host works on the request's stored draft again after a failed check or a review with findings, and sends the request back to
/// checking, see <c>docs/reference/authoring-requests.md#states</c>. Each move to <c>fixing</c> adds 1 to <c>round</c>, and a request still failing when
/// <c>Ai:FixRounds</c> are used up stops as <c>failed</c> with its draft kept, see <c>docs/reference/authoring-requests.md#budgets</c>.
/// The model is the scripted adapter and the publisher runs beside the host as it does in production.
/// </summary>
public sealed class AgentFixingTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly TimeSpan _runs = TimeSpan.FromMinutes(5);

    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-agent-fixing-revisions-").FullName;
    private AuthoringRealm _realm = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_that_fails_its_compile_check_is_fixed_then_checked_and_reviewed_and_the_request_is_ready_in_round_1_and_the_fix_step_is_not_offered_run_scenarios()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'A plan.' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter(
        [
            // The layer of the working step adds a step that nothing reaches, which does not compile.
            ToolUse("layer-1", "write_layer", LayerInput(), 100, 20),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
            new ModelAnswer(
                [
                    new ModelToolCall("run-1", "run_scenarios", AgentPlanningTests.Json("""{"application":"PurchaseRequests"}""")),
                    new ModelToolCall("layer-2", "write_layer", AgentPlanningTests.Json(LayerInput(DueInThreeDays))),
                    new ModelToolCall("scenario-1", "write_resource", AgentPlanningTests.Json(ScenarioInput())),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(100, 20)),
            new ModelAnswer([new ModelText("Fixed.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
            Plan("PASSED\nThe draft does the request."),
        ]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await using var publisher = StudioPublishTests.RunPublisher(Tenant, Host, _realm.SchemaConnection);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "ready", "The host did not make the request ready.", _runs);
        await host.StopAsync(CancellationToken);

        Assert.Equal("1", await ScalarAsync($"SELECT round FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("work,check:failed,fix,check:passed,review:passed", await StepsAsync(id));
        Assert.Equal(5, script.Calls.Count);

        // The first message of the fix step holds the compile diagnostics of the failed check, and the step offers the tools of working.
        var code = await ScalarAsync($"SELECT checks->'compile'->'diagnostics'->0->>'code' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check' ORDER BY ordinal LIMIT 1");
        Assert.NotEmpty(code);
        var first = Assert.IsType<ModelText>(Assert.Single(script.Calls[2].Messages[0].Content)).Text;
        Assert.Contains("What failed in the last check:", first, StringComparison.Ordinal);
        Assert.Contains("Compile diagnostics:", first, StringComparison.Ordinal);
        Assert.Contains(code, first, StringComparison.Ordinal);
        Assert.Contains("write_layer", script.Calls[2].Tools.Select(tool => tool.Name));
        Assert.DoesNotContain("run_scenarios", script.Calls[2].Tools.Select(tool => tool.Name));

        // A tool that is not offered gets a tool error, and the writes of the same answer still reach the draft.
        var results = script.Calls[3].Messages[^1].Content.OfType<ModelToolResult>().ToList();
        Assert.Equal(["run-1", "layer-2", "scenario-1"], results.Select(result => result.CallId));
        Assert.True(results[0].IsError);
        Assert.Equal("The tool is not offered in fixing.", results[0].Content);
        Assert.False(results[1].IsError, results[1].Content);
        Assert.False(results[2].IsError, results[2].Content);
    }

    [Fact]
    public async Task With_one_fix_round_a_request_still_failing_after_it_is_failed_in_round_1_and_keeps_its_draft_which_the_author_can_still_read()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'A plan.' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter(
        [
            ToolUse("layer-1", "write_layer", LayerInput(), 100, 20),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
            // The fix ends its turn with no change, so the compile fails again. A failed compile needs no publisher.
            new ModelAnswer([new ModelText("Nothing to change.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
        ]);
        var settings = new Dictionary<string, string?> { ["Ai:FixRounds"] = "1" };
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()), settings);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1", await ScalarAsync($"SELECT round FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("work,check:failed,fix,check:failed", await StepsAsync(id));
        Assert.Equal(3, script.Calls.Count);
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("true", await ScalarAsync($"SELECT (finished_at IS NOT NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        var draft = await ScalarAsync($"SELECT coalesce(draft_id::text, '') FROM axis.authoring_requests WHERE id = '{id}'");
        Assert.True(Guid.TryParse(draft, out _), "The request holds no draft.");
        using var diff = await StudioPublishTests.SendAsync(client, HttpMethod.Get, $"/api/studio/apps/PurchaseRequests/drafts/{draft}/diff", Host);
        Assert.Equal(HttpStatusCode.OK, diff.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        _realm = new AuthoringRealm(keycloak, database, configure: Configure);
        await _realm.InitializeAsync();

        // The publisher serves a tenant only when its database holds every migration, the processes module's too.
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_realm.SchemaConnection).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(_realm.SchemaConnection, CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _realm.DisposeAsync();
        Directory.Delete(_revisions, recursive: true);
    }

    // The draft needs the history of the application's folder, and the layer needs the module the application pins, as AgentWorkingTests sets them.
    private void Configure(IWebHostBuilder builder, string folder)
    {
        builder.UseSetting("Studio:RevisionsPath", _revisions);
        builder.UseSetting("Modules:Path", SampleModules.Path);
        builder.UseSetting("Extensions:Path", SamplePackages.Path);
    }

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    // The kinds of the steps in order, each with its outcome when it has one.
    private Task<string> StepsAsync(Guid id) =>
        ScalarAsync($"SELECT string_agg(kind || coalesce(':' || outcome, ''), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'");
}
