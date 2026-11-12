using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Agent.Models;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AgentWorkingTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The checking step of the agent host compiles the request's draft and runs every scenario of the application and its modules on it through the
/// publisher, see <c>docs/reference/authoring-requests.md#states</c>. It stores <c>{ compile, scenarios }</c> on its <c>check</c> step, and the read route of the
/// request answers them, see <c>docs/reference/authoring-request-storage.md#authoring-requests</c>. A request that passes its checks goes on to the review step.
/// The model is the scripted adapter and the publisher runs beside the host as it does in production.
/// </summary>
public sealed class AgentCheckingTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly TimeSpan _runs = TimeSpan.FromMinutes(5);

    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-agent-checking-revisions-").FullName;
    private AuthoringRealm _realm = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_approved_request_that_passes_every_check_records_them_is_reviewed_and_a_review_that_answers_PASSED_makes_it_ready_and_the_read_route_answers_both()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        var script = new ScriptedModelAdapter(
        [
            Plan("Add a finance review step through the layer and a scenario for it."),
            new ModelAnswer(
                [
                    new ModelToolCall("layer-1", "write_layer", AgentPlanningTests.Json(LayerInput(DueInThreeDays))),
                    new ModelToolCall("scenario-1", "write_resource", AgentPlanningTests.Json(ScenarioInput())),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(100, 20)),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
            Plan("PASSED\nThe draft does the request."),
        ]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not write the plan.");
        using var approved = await StudioPublishTests.SendAsync(client, HttpMethod.Post, $"{Url}/{id}/approve", Host);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await using var publisher = StudioPublishTests.RunPublisher(Tenant, Host, _realm.SchemaConnection);
        await CheckFinishedAsync(id);
        await ReviewFinishedAsync(id);
        await host.StopAsync(CancellationToken);

        Assert.True(await StateAsync(id) == "ready", await ChecksAsync(id));
        Assert.Equal("passed", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'review'"));
        Assert.Equal("passed", await ScalarAsync($"SELECT checks->'review'->>'outcome' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'review'"));

        Assert.Equal("passed", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));
        Assert.Equal("passed", await ScalarAsync($"SELECT checks->'compile'->>'outcome' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));
        Assert.Equal("passed", await ScalarAsync($"SELECT checks->'scenarios'->>'outcome' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));

        // The read route answers the checks of the check step, and null on the steps before it.
        using var read = await StudioPublishTests.SendAsync(client, HttpMethod.Get, $"{Url}/{id}", Host);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var request = await StudioPublishTests.ReadAsync(read);
        var steps = request.RootElement.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["plan", "work", "check", "review"], steps.Select(step => step.GetProperty("kind").GetString()));
        Assert.Equal(JsonValueKind.Null, steps[0].GetProperty("checks").ValueKind);
        Assert.Equal(JsonValueKind.Null, steps[1].GetProperty("checks").ValueKind);
        var checks = steps[2].GetProperty("checks");
        Assert.Equal("passed", checks.GetProperty("compile").GetProperty("outcome").GetString());
        var scenarios = checks.GetProperty("scenarios");
        Assert.Equal("passed", scenarios.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.String, scenarios.GetProperty("request").ValueKind);
        var items = scenarios.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("outcome").GetString());
        Assert.Equal("passed", items["FinanceReviewAdded"]);
        Assert.Equal("passed", items["department-budgets/ApprovalRecordsDecision"]);
        Assert.DoesNotContain(items.Values, outcome => outcome == "failed");
        Assert.Equal("passed", steps[3].GetProperty("checks").GetProperty("review").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_layer_that_breaks_a_module_scenario_records_it_as_failed_with_its_step_and_moves_the_request_to_fixing()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'A plan.' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter(
        [
            ToolUse("layer-1", "write_layer", LayerInput(Reroute), 100, 20),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
        ]);
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()), fixing: false);
        await using var publisher = StudioPublishTests.RunPublisher(Tenant, Host, _realm.SchemaConnection);
        await host.StartAsync(CancellationToken);
        await CheckFinishedAsync(id);
        await host.StopAsync(CancellationToken);

        Assert.True(await StateAsync(id) == "fixing", await ChecksAsync(id));

        Assert.Equal("failed", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));
        Assert.Equal("passed", await ScalarAsync($"SELECT checks->'compile'->>'outcome' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));
        Assert.Equal("failed", await ScalarAsync($"SELECT checks->'scenarios'->>'outcome' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));
        var items = JsonNode.Parse(await ScalarAsync($"SELECT checks->'scenarios'->'items' FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"))!.AsArray();
        var broken = Assert.Single(items, item => item!["name"]!.GetValue<string>() == "department-budgets/ApprovalRecordsDecision")!;
        Assert.Equal("failed", broken["outcome"]!.GetValue<string>());
        // The step is the 0-based index of the failing step: expectInstance, the fourth step of the scenario.
        Assert.Equal(3, broken["step"]!.GetValue<int>());
        Assert.Equal("/steps/3", broken["path"]!.GetValue<string>());
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

    // The layer of the working test adds a step that nothing reaches, which does not compile. This one keeps the process as it is and gives the review a due time.
    internal static void DueInThreeDays(JsonNode process)
    {
        var steps = (JsonArray)process["steps"]!;
        steps.Remove(steps.Single(step => step!["name"]!.GetValue<string>() == "financeReview"));
        steps.OfType<JsonObject>().Single(step => step["name"]!.GetValue<string>() == "review")["dueIn"] = "P3D";
    }

    // Sends the approval to the finance review step that the layer adds, which no scenario completes, so ApprovalRecordsDecision no longer ends its process.
    private static void Reroute(JsonNode process)
    {
        var steps = (JsonArray)process["steps"]!;
        var review = steps.OfType<JsonObject>().Single(step => step["name"]!.GetValue<string>() == "review");
        review["outcomes"]!.AsArray().OfType<JsonObject>().Single(outcome => outcome["name"]!.GetValue<string>() == "approve")["next"] = "financeReview";
    }

    // The step's row is finished in the transaction that moves the request on, and a run of every scenario takes minutes.
    private Task CheckFinishedAsync(Guid id) =>
        WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check' AND finished_at IS NOT NULL") == "1",
            "The host did not finish the check step.",
            _runs);

    // The review step's row is finished in the transaction that moves the request on.
    private Task ReviewFinishedAsync(Guid id) =>
        WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'review' AND finished_at IS NOT NULL") == "1",
            "The host did not finish the review step.");

    // The stored checks, as the message of an assertion that fails.
    private Task<string> ChecksAsync(Guid id) =>
        ScalarAsync($"SELECT 'state ' || (SELECT state FROM axis.authoring_requests WHERE id = '{id}') || ': ' || coalesce(string_agg(outcome || ' ' || coalesce(checks::text, ''), '; '), '') FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'");

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");
}
