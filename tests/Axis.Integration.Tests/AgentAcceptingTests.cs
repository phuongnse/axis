using System.Net;
using System.Text.Json.Nodes;
using Axis.Agent;
using Axis.Agent.Models;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AgentWorkingTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The accept step of the agent host asks for the accept of the draft of a <c>ready</c> request that asked for auto-accept, and waits until the accept ends,
/// see <c>docs/reference/authoring-requests.md#accepting</c>. A success ends the request <c>accepted</c>, a refusal of <c>accept_draft</c> leaves it <c>ready</c>
/// and it is not claimed again, a conflict sends it back to <c>working</c> with no draft, and any other failure stops it <c>failed</c> with the outcome
/// <c>notAccepted</c>. The model is the scripted adapter and the publisher runs beside the host as it does in production. Each test first makes a real draft of
/// the author with a host that has no checking step, and then moves the request to <c>ready</c> with a passed check and review.
/// </summary>
public sealed class AgentAcceptingTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string SendWithAutoAccept = """{ "text": "Add a cost centre to every purchase request.", "autoAccept": true }""";
    private const string SupplierFile = "entities/supplier.json";
    private const string SupplierId = "2deb5acd-dfd8-4c5a-b546-a1aa03df6964";

    private static readonly TimeSpan _runs = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(1);

    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-agent-accepting-revisions-").FullName;
    private AuthoringRealm _realm = null!;
    private string _folder = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_accept_that_meets_a_conflict_sends_the_request_back_to_working_with_no_draft_and_it_gets_a_new_draft_a_new_passed_review_and_is_accepted()
    {
        using var client = await _realm.SignInAsync();
        var (id, first) = await SeedReadyAsync(client, ReadAndWrite());
        // The draft read the supplier, so a change to its file on main is a conflict when the publisher accepts it.
        File.AppendAllText(Path.Combine(_folder, SupplierFile), "\n");
        var script = new ScriptedModelAdapter([.. ReadAndWrite(), Plan("PASSED\nThe draft does the request.")]);
        await using var publisher = RunPublisher();
        await using var host = BuildAgent(_realm, script, new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept' AND outcome = 'accepted'") == "1"
                || await StateAsync(id) == "failed",
            "The host did not accept the request or stop it.",
            _runs);
        await host.StopAsync(CancellationToken);

        Assert.True(await StateAsync(id) == "accepted", await DescribeAsync(id));
        Assert.Equal(
            "work:-,check:passed,review:passed,accept:conflict,work:-,check:passed,review:passed,accept:accepted",
            await ScalarAsync($"SELECT string_agg(kind || ':' || coalesce(outcome, '-'), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'"));
        // The first accept failed with the conflict, the second is of a new draft, and the request keeps the new one.
        Assert.Equal(
            DiagnosticCodes.AcceptConflict,
            await ScalarAsync($"SELECT diagnostics->0->>'code' FROM axis.publish_requests WHERE kind = 'accept' AND status = 'failed' AND draft_id = '{first}'"));
        var second = await ScalarAsync($"SELECT draft_id FROM axis.authoring_requests WHERE id = '{id}'");
        Assert.NotEqual(first.ToString("D"), second);
        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM axis.publish_requests WHERE kind = 'accept' AND status = 'succeeded' AND draft_id = '{second}'"));
        Assert.Equal("2", await ScalarAsync("SELECT count(DISTINCT draft_id) FROM axis.publish_requests WHERE kind = 'accept'"));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        // A conflict leaves the round as it is, because the request went back to working and not to fixing.
        Assert.Equal("0", await ScalarAsync($"SELECT round FROM axis.authoring_requests WHERE id = '{id}'"));
    }

    [Fact]
    public async Task When_the_agents_accept_succeeds_the_request_ends_accepted_its_accept_step_has_the_outcome_accepted_and_its_token_is_erased()
    {
        using var client = await _realm.SignInAsync();
        var (id, draft) = await SeedReadyAsync(client, ReadAndWrite());
        await using var publisher = RunPublisher();
        await using var host = BuildAgent(_realm, new ScriptedModelAdapter([]), new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept' AND finished_at IS NOT NULL") == "1",
            "The host did not finish the accept step.",
            _runs);
        await host.StopAsync(CancellationToken);

        Assert.True(await StateAsync(id) == "accepted", await DescribeAsync(id));
        Assert.Equal("accepted", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept'"));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL AND claimed_by IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal(draft.ToString("D"), await ScalarAsync($"SELECT draft_id FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM axis.publish_requests WHERE kind = 'accept' AND status = 'succeeded' AND draft_id = '{draft}'"));
        Assert.Equal("1", await ScalarAsync("SELECT count(*) FROM axis.audit_records WHERE action = 'authoringRequest.accepted'"));
    }

    [Fact]
    public async Task When_accept_draft_refuses_because_auto_accept_is_off_the_request_stays_ready_with_one_refused_accept_step_and_is_not_claimed_again()
    {
        using var client = await _realm.SignInAsync();
        var (id, _) = await SeedReadyAsync(client, ReadAndWrite());
        await ScalarAsync($"UPDATE axis.application_settings SET auto_accept = false WHERE application_id = (SELECT application_id FROM axis.authoring_requests WHERE id = '{id}') RETURNING 1");
        await using var host = BuildAgent(_realm, new ScriptedModelAdapter([]), new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept' AND finished_at IS NOT NULL") == "1",
            "The host did not finish the accept step.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("ready", await StateAsync(id));
        Assert.Equal("refused", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept'"));
        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept'"));
        Assert.Equal("0", await ScalarAsync("SELECT count(*) FROM axis.publish_requests WHERE kind = 'accept'"));
        // The request keeps its token, so the author can still accept or drop it.
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NOT NULL AND claimed_by IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Null(await StoreOf(host).ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken));
    }

    [Fact]
    public async Task An_accept_that_fails_without_a_conflict_stops_the_request_failed_with_the_outcome_notAccepted_and_keeps_its_draft()
    {
        using var client = await _realm.SignInAsync();
        // The layer adds a step that nothing reaches, so the draft does not compile.
        var (id, draft) = await SeedReadyAsync(client, [ToolUse("layer-1", "write_layer", LayerInput(), 10, 5), EndTurn()]);
        await using var publisher = RunPublisher();
        await using var host = BuildAgent(_realm, new ScriptedModelAdapter([]), new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.", _runs);
        await host.StopAsync(CancellationToken);

        Assert.Equal("notAccepted", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'accept'"));
        Assert.Equal(draft.ToString("D"), await ScalarAsync($"SELECT draft_id FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.Equal("failed", await ScalarAsync($"SELECT status FROM axis.publish_requests WHERE kind = 'accept' AND draft_id = '{draft}'"));
        Assert.DoesNotContain(
            DiagnosticCodes.AcceptConflict,
            await ScalarAsync($"SELECT diagnostics::text FROM axis.publish_requests WHERE kind = 'accept' AND draft_id = '{draft}'"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_claim_of_ready_finds_a_request_only_when_it_asked_for_auto_accept_has_a_draft_and_no_accept_step_finished_since_its_newest_review()
    {
        await using var host = BuildAgent(_realm, new ScriptedModelAdapter([]), new RecordingHandler(_realm.Factory.Server.CreateHandler()));
        var store = StoreOf(host);

        // The last check and review passed in every case, so only the rule of the claim decides.
        var withoutAutoAccept = await InsertReadyAsync(autoAccept: false);
        Assert.Null(await store.ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken));
        Assert.Empty(await store.ListNeedingWorkAsync(Tenant, CancellationToken));
        await EndAsync(withoutAutoAccept);

        var withoutDraft = await InsertReadyAsync(draft: false);
        Assert.Null(await store.ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken));
        await EndAsync(withoutDraft);

        var refused = await InsertReadyAsync(extraSteps: [("accept", "refused")]);
        Assert.Null(await store.ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken));
        await EndAsync(refused);

        // The same request, whatever the state, is only claimed for a state the host asks for.
        var allowed = await InsertReadyAsync();
        Assert.Null(await store.ClaimAsync(Tenant, ["working"], "probe", _lease, CancellationToken));
        Assert.Equal(allowed, Assert.Single(await store.ListNeedingWorkAsync(Tenant, CancellationToken)).Id);
        var claimed = await store.ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken);
        Assert.Equal(allowed, claimed?.Id);
        Assert.Equal("ready", claimed?.State);
        await EndAsync(allowed);

        // An accept step that was interrupted does not count, and a review after a finished accept lets the request be accepted again.
        var interrupted = await InsertReadyAsync(extraSteps: [("accept", "interrupted")]);
        Assert.Equal(interrupted, (await store.ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken))?.Id);
        await EndAsync(interrupted);

        var redone = await InsertReadyAsync(extraSteps: [("accept", "conflict"), ("work", null), ("check", "passed"), ("review", "passed")]);
        Assert.Equal(redone, (await store.ClaimAsync(Tenant, ["ready"], "probe", _lease, CancellationToken))?.Id);
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

    // The draft needs the history of the application's folder, and the layer needs the module the application pins, as AgentCheckingTests sets them.
    private void Configure(IWebHostBuilder builder, string folder)
    {
        _folder = folder;
        builder.UseSetting("Studio:RevisionsPath", _revisions);
        builder.UseSetting("Modules:Path", SampleModules.Path);
        builder.UseSetting("Extensions:Path", SamplePackages.Path);
    }

    private StudioPublishTests.RunningPublisher RunPublisher() =>
        StudioPublishTests.RunPublisher(Tenant, Host, _realm.SchemaConnection, studio: (_folder, _revisions));

    private static AuthoringRequestStore StoreOf(WebApplication host) => host.Services.GetRequiredService<AuthoringRequestStore>();

    private static ModelAnswer EndTurn() => Plan("Done.", 1, 1);

    // The working step reads the supplier in its draft, which the draft records, and adds a scenario.
    private static ModelAnswer[] ReadAndWrite() =>
    [
        ToolUse("read-1", "read_resource", $$"""{"application":"PurchaseRequests","id":"{{SupplierId}}"}""", 10, 5),
        ToolUse("write-1", "write_resource", ScenarioInput(), 10, 5),
        EndTurn(),
    ];

    // Makes a request that asked for auto-accept with a real draft of the author, as a host without a checking step leaves it, and then moves it to ready
    // with a passed check and review, with the application's auto-accept on.
    private async Task<(Guid Id, Guid Draft)> SeedReadyAsync(HttpClient client, ModelAnswer[] work)
    {
        using var sent = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, SendWithAutoAccept);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        using var body = await StudioPublishTests.ReadAsync(sent);
        var id = Guid.Parse(body.RootElement.GetProperty("id").GetString()!);
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'A plan.' WHERE id = '{id}' RETURNING 1");
        await using (var host = BuildAgent(_realm, new ScriptedModelAdapter(work), new RecordingHandler(_realm.Factory.Server.CreateHandler()), without: ["CheckingStep"]))
        {
            await host.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await StateAsync(id) == "checking", "The host did not make the draft.");
            await host.StopAsync(CancellationToken);
        }

        var draft = Guid.Parse(await ScalarAsync($"SELECT draft_id FROM axis.authoring_requests WHERE id = '{id}'"));
        await ScalarAsync(
            $"INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, outcome, started_at, finished_at) "
            + $"SELECT gen_random_uuid(), '{id}', (SELECT max(ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}') + n, kind, 'passed', now(), now() "
            + "FROM (VALUES (1, 'check'), (2, 'review')) AS rounds (n, kind) RETURNING 1");
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'ready' WHERE id = '{id}' RETURNING 1");
        await ScalarAsync(
            $"INSERT INTO axis.application_settings (application_id, auto_accept) SELECT application_id, true FROM axis.authoring_requests WHERE id = '{id}' "
            + "ON CONFLICT (application_id) DO UPDATE SET auto_accept = true");
        return (id, draft);
    }

    // A ready request of its own, with a passed check and review and then the extra steps in order. An outcome of null is no outcome.
    private async Task<Guid> InsertReadyAsync(bool autoAccept = true, bool draft = true, (string Kind, string? Outcome)[]? extraSteps = null)
    {
        var id = Guid.NewGuid();
        await ScalarAsync(
            "INSERT INTO axis.authoring_requests (id, application_id, author, text, state, draft_id, auto_accept) "
            + $"VALUES ('{id}', '{Guid.NewGuid()}', 'pat', 'Add a field.', 'ready', {(draft ? $"'{Guid.NewGuid()}'" : "NULL")}, {autoAccept.ToString().ToLowerInvariant()}) RETURNING 1");
        List<(string Kind, string? Outcome)> steps = [("check", "passed"), ("review", "passed"), .. extraSteps ?? []];
        for (var ordinal = 1; ordinal <= steps.Count; ordinal++)
        {
            var (kind, outcome) = steps[ordinal - 1];
            await ScalarAsync(
                "INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, outcome, started_at, finished_at) "
                + $"VALUES ('{Guid.NewGuid()}', '{id}', {ordinal}, '{kind}', {(outcome is null ? "NULL" : $"'{outcome}'")}, now(), now()) RETURNING 1");
        }

        return id;
    }

    // The request is dropped, so a later claim does not find it.
    private async Task EndAsync(Guid id) => await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'dropped' WHERE id = '{id}' RETURNING 1");

    // The steps and the state, as the message of an assertion that fails.
    private Task<string> DescribeAsync(Guid id) =>
        ScalarAsync(
            $"SELECT 'state ' || (SELECT state FROM axis.authoring_requests WHERE id = '{id}') || ': ' || coalesce(string_agg(kind || ':' || coalesce(outcome, '-'), ',' ORDER BY ordinal), '') "
            + $"|| ' ' || coalesce((SELECT string_agg(kind || ':' || status || ' ' || coalesce(diagnostics::text, ''), '; ') FROM axis.publish_requests), '') FROM axis.authoring_request_steps WHERE request_id = '{id}'");

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");
}
