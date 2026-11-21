using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Axis.Agent.Models;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using static Axis.Integration.Tests.AgentPlanningTests;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// A request of the agent host writes an application's package, checking tests it and a failing test moves the request to <c>fixing</c>, see
/// <c>docs/reference/authoring-requests.md#states</c>. The tools act for the author, so an author without the developer role is refused by the MCP
/// server and the model gets the refusal as a tool error. The Keycloak user of <see cref="AuthoringRealm"/> holds only the author role, so a test makes
/// it the developer role with <c>Studio:DeveloperRole</c> to allow the tools, and another role name to refuse them. The model is the scripted adapter.
/// The tests use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[Collection(PackageBuilds.Name)]
[UnsupportedOSPlatform("windows")]
public sealed class AgentPackageTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly TimeSpan _runs = TimeSpan.FromMinutes(5);

    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-agent-package-revisions-").FullName;
    private AuthoringRealm? _realm;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private AuthoringRealm Realm => _realm ?? throw new InvalidOperationException("The realm is not started.");

    [Fact]
    public async Task A_request_that_writes_a_package_with_a_failing_test_stores_the_package_check_as_failed_and_moves_to_fixing()
    {
        await StartAsync(developerRole: "author");
        using var client = await Realm.SignInAsync();
        var id = await SendAsync(client);
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'Add a tools package.' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter(
        [
            new ModelAnswer(
                [
                    PackageFile("manifest-1", "axis-package.json", McpTestPackageTests.Manifest),
                    PackageFile("adapter-1", "adapter.py", "import sys\n"),
                    PackageFile("lock-1", "requirements.txt", McpTestPackageTests.NoDependencies),
                    PackageFile("tests-1", "test_tools.py", McpTestPackageTests.ToolsTests),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(100, 20)),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(30, 10)),
        ]);
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var packageHost = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        await using var host = BuildAgent(Realm, script, new RecordingHandler(Realm.Factory.Server.CreateHandler()), fixing: false);
        await using var publisher = StudioPublishTests.RunPublisher(
            Tenant,
            Host,
            Realm.SchemaConnection,
            extra: new Dictionary<string, string?>
            {
                ["Extensions:ApplicationPackages"] = "true",
                ["Extensions:Runners:Application"] = packageHost.Address,
                ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
            });
        await host.StartAsync(CancellationToken);
        await StepFinishedAsync(id, "check", _runs);
        await host.StopAsync(CancellationToken);

        // The scenario outcome is not asserted: the scenario run may also try to build the package.
        Assert.True(await StateAsync(id) == "fixing", await ChecksAsync(id));
        Assert.Equal("failed", await ScalarAsync($"SELECT outcome FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'"));
        Assert.Equal("failed", await CheckAsync(id, "->'packages'->>'outcome'"));
        var items = JsonNode.Parse(await CheckAsync(id, "->'packages'->'items'"))!.AsArray();
        var tested = Assert.Single(items)!;
        Assert.Equal("tools", tested["package"]!.GetValue<string>());
        Assert.Equal("failed", tested["status"]!.GetValue<string>());
        var failing = Assert.Single(tested["tests"]!.AsArray(), test => test!["name"]!.GetValue<string>() == "test_tools.ToolsCase.test_fails")!;
        Assert.Equal("failed", failing["outcome"]!.GetValue<string>());
        var draft = await ScalarAsync($"SELECT draft_id FROM axis.authoring_requests WHERE id = '{id}'");
        Assert.Equal(
            "4",
            await ScalarAsync($"SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'tool' = 'write_package_file' AND details->>'draftId' = '{draft}'"));
    }

    [Fact]
    public async Task An_author_without_the_developer_role_gets_the_refusal_of_the_server_as_a_tool_error_and_no_package_file_is_written()
    {
        await StartAsync(developerRole: "developer");
        using var client = await Realm.SignInAsync();
        var id = await SendAsync(client);
        await ScalarAsync($"UPDATE axis.authoring_requests SET state = 'working', plan = 'Add a tools package.' WHERE id = '{id}' RETURNING 1");
        var script = new ScriptedModelAdapter(
        [
            new ModelAnswer([PackageFile("manifest-1", "axis-package.json", McpTestPackageTests.Manifest)], ModelStopReason.ToolUse, new ModelTokens(10, 5)),
            new ModelAnswer([new ModelText("Done.")], ModelStopReason.EndTurn, new ModelTokens(10, 5)),
        ]);
        var server = new RecordingHandler(Realm.Factory.Server.CreateHandler());
        await using var host = BuildAgent(Realm, script, server, fixing: false);
        await host.StartAsync(CancellationToken);
        await StepFinishedAsync(id, "work", TimeSpan.FromSeconds(60));
        await host.StopAsync(CancellationToken);

        var result = Assert.Single(script.Calls[1].Messages[^1].Content.OfType<ModelToolResult>());
        Assert.Equal("manifest-1", result.CallId);
        Assert.True(result.IsError);
        Assert.Equal("You do not hold the developer role.", result.Content);
        Assert.Contains("write_package_file", server.ToolCalls);
        Assert.Equal(
            "0",
            await ScalarAsync("SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'tool' = 'write_package_file'"));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_realm is not null)
        {
            await _realm.DisposeAsync();
        }

        Directory.Delete(_revisions, recursive: true);
    }

    // Builds the realm of the test: its server allows application packages for the tenant, and its developer role is the Keycloak role developerRole.
    private async Task StartAsync(string developerRole)
    {
        _realm = new AuthoringRealm(keycloak, database, configure: (builder, _) => Configure(builder, developerRole));
        await _realm.InitializeAsync();

        // The publisher serves a tenant only when its database holds every migration, the processes module's too.
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_realm.SchemaConnection).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(_realm.SchemaConnection, CancellationToken);
    }

    // The draft needs the history of the application's folder, as AgentCheckingTests sets it.
    private void Configure(IWebHostBuilder builder, string developerRole)
    {
        builder.UseSetting("Studio:RevisionsPath", _revisions);
        builder.UseSetting("Modules:Path", SampleModules.Path);
        builder.UseSetting("Extensions:Path", SamplePackages.Path);
        builder.UseSetting("Studio:DeveloperRole", developerRole);

        // The configuration source has no switch, so the host turns it on for every tenant.
        builder.ConfigureTestServices(PackagesOnCatalog.Switch);
    }

    // The model leaves out the draft, so the agent host fills in the request's.
    private static ModelToolCall PackageFile(string callId, string path, string content) =>
        new(
            callId,
            "write_package_file",
            AgentPlanningTests.Json(new JsonObject { ["application"] = "PurchaseRequests", ["package"] = "tools", ["path"] = path, ["content"] = content }.ToJsonString()));

    // The step's row is finished in the transaction that moves the request on.
    private Task StepFinishedAsync(Guid id, string kind, TimeSpan timeout) =>
        WaitUntilAsync(
            async () => await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = '{kind}' AND finished_at IS NOT NULL") == "1",
            $"The host did not finish the {kind} step.",
            timeout);

    // A part of the check step's checks, such as "->'packages'->>'outcome'".
    private Task<string> CheckAsync(Guid id, string part) =>
        ScalarAsync($"SELECT checks{part} FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'");

    // The stored checks, as the message of an assertion that fails.
    private Task<string> ChecksAsync(Guid id) =>
        ScalarAsync($"SELECT 'state ' || (SELECT state FROM axis.authoring_requests WHERE id = '{id}') || ': ' || coalesce(string_agg(outcome || ' ' || coalesce(checks::text, ''), '; '), '') FROM axis.authoring_request_steps WHERE request_id = '{id}' AND kind = 'check'");

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(Realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");
}
