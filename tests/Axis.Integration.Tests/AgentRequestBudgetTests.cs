using System.Net;
using Axis.Agent;
using Axis.Agent.Models;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The token and time budgets of an authoring request, see <c>docs/reference/authoring-requests.md#budgets</c>. A request that would pass
/// <c>Ai:RequestTokenBudget</c> makes no further model call, and one past <c>Ai:RequestTimeout</c> from its approval, or still running at it, is cancelled.
/// Either stops as <c>failed</c> with the outcome <c>overBudget</c> or <c>timedOut</c>, and keeps its draft. The model is the scripted adapter.
/// </summary>
public sealed class AgentRequestBudgetTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly AuthoringLoopOptions _options = new(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-agent-budget-revisions-").FullName;
    private AuthoringRealm _realm = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_that_would_pass_the_token_budget_is_not_sent_and_the_request_stops_failed_with_the_outcome_overBudget_and_keeps_its_draft()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ApprovedAsync(id, "now()");
        // The default output limit is 16000. The first call fits, and its input of 3000 and the next output limit pass 20000.
        var script = new ScriptedModelAdapter(
        [
            ToolUse("call-1", "read_names", """{"application":"PurchaseRequests"}""", 3000, 100),
            Plan("Never sent."),
        ]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(script, server, new() { ["Ai:RequestTokenBudget"] = "20000" });
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Single(script.Calls);
        Assert.Matches(@"^\d+:work:overBudget:3100:true$", await StepsAsync(id));
        Assert.True(Guid.TryParse(await ScalarAsync($"SELECT coalesce(draft_id::text, '') FROM axis.authoring_requests WHERE id = '{id}'"), out _), "The request lost its draft.");
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_request_approved_longer_ago_than_the_time_limit_makes_no_call_and_stops_failed_with_the_outcome_timedOut()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ApprovedAsync(id, "now() - interval '31 minutes'");
        var script = new ScriptedModelAdapter([Plan("Never sent.")]);
        var server = new RecordingHandler(_realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(script, server, new() { ["Ai:RequestTimeout"] = "00:30:00" });
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Empty(script.Calls);
        Assert.Empty(server.ToolCalls);
        Assert.Equal("1:work:timedOut:0:true", await StepsAsync(id));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_step_still_running_at_the_time_limit_is_cancelled_and_the_request_stops_failed_with_the_outcome_timedOut()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await ApprovedAsync(id, "now()");
        var running = new TestStep("working", "work", "checking", (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var script = new ScriptedModelAdapter([]);
        await using var host = BuildAgent(script, new RecordingHandler(_realm.Factory.Server.CreateHandler()), new() { ["Ai:RequestTimeout"] = "00:00:02" }, running);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        // A host that started after the limit stops the request before the step, and the row reads the same.
        Assert.Equal("1:work:timedOut:0:true", await StepsAsync(id));
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

    // The draft needs the history of the application's folder, as AgentWorkingTests sets it.
    private void Configure(IWebHostBuilder builder, string folder)
    {
        builder.UseSetting("Studio:RevisionsPath", _revisions);
        builder.UseSetting("Modules:Path", SampleModules.Path);
        builder.UseSetting("Extensions:Path", SamplePackages.Path);
    }

    private WebApplication BuildAgent(ScriptedModelAdapter script, RecordingHandler server, Dictionary<string, string?> extra, IAuthoringStep? step = null)
    {
        var settings = _realm.AgentSettings();
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return AgentTestHost.Build(
            aiOn: true,
            settings,
            new LogCollector(),
            services =>
            {
                services.AddSingleton(_options);
                services.AddSingleton(script);
                services.AddHttpClient(AuthorMcpClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
                if (step is not null)
                {
                    AgentTestHost.Steps(_options, step)(services);
                }
            });
    }

    // The request is approved without the step before it, as the approval leaves it. The approval's time is an SQL expression.
    private async Task ApprovedAsync(Guid id, string approvedAt) =>
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'A plan.', approved_at = {approvedAt} WHERE id = '{id}' RETURNING 1");

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> StepsAsync(Guid id) =>
        ScalarAsync(
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'");
}
