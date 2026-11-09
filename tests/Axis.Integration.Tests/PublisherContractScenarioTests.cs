using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Tenancy.Platform;
using Npgsql;
using static Axis.Integration.Tests.ContractTestModules;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>contracts</c> step of a publish and a validate request: the contract scenarios of the pinned module run in their own
/// databases before the compile. The <c>requests</c> test module is copied to a temporary folder with one contract scenario that
/// covers its <c>submit</c> transition, because the shared fixture is used by other tests. The built-in <c>organization</c> module,
/// which the test project copies to <c>BuiltInModules</c>, runs its own five contract scenarios, and the built-in <c>approvals</c>
/// module runs its seven. A copy of the built-in modules whose approval process approves right after the submit makes the publish fail.
/// </summary>
[Collection("Test scenario runs")]
public sealed class PublisherContractScenarioTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string ScenarioFile = "modules/requests/scenarios/submit-request.json";

    private static readonly string _app = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp");
    private static readonly string _organizationApp = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OrganizationApp");
    private static readonly string _approvalsApp = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ApprovalsModuleApp");

    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(180);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // A database that an earlier test left behind must not fail this test, so each test starts from none.
    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(await PostgreSqlServer.AdminConnectionStringAsync());
        await connection.OpenAsync(CancellationToken);
        var names = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT d.datname FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba WHERE r.rolname = 'axis_scenario'", connection))
        await using (var reader = await list.ExecuteReaderAsync(CancellationToken))
        {
            while (await reader.ReadAsync(CancellationToken))
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task A_publish_whose_contract_scenario_fails_fails_with_AXC0342_and_stores_nothing()
    {
        using var modules = ModulesWithScenario("approved");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish", _app);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0342", error["code"]!.GetValue<string>());
        Assert.Equal(ScenarioFile, error["file"]!.GetValue<string>());
        Assert.Equal("/steps/2", error["path"]!.GetValue<string>());
        var message = error["message"]!.GetValue<string>();
        Assert.Contains("'requests'", message, StringComparison.Ordinal);
        Assert.Contains("'SubmitRequest'", message, StringComparison.Ordinal);
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} contracts failed: AXC0342 ", StringComparison.Ordinal));
        Assert.Equal(["version ok"], StepLines(result, tenant).Where(line => line.EndsWith(" ok", StringComparison.Ordinal)).ToArray());
        Assert.Null(await ScalarAsync(scenario, tenant, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        var item = Assert.Single(await ResultsAsync(scenario, tenant, request))!;
        Assert.Equal("failed", item["outcome"]!.GetValue<string>());
        Assert.Equal(2, item["step"]!.GetValue<int>());
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_whose_contract_scenario_passes_activates_and_stores_the_outcome()
    {
        using var modules = ModulesWithScenario("submitted");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish", _app);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        var status = await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
        Assert.True(status is "succeeded", string.Join('\n', result.Output));
        Assert.NotNull(await ScalarAsync(scenario, tenant, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        var item = Assert.Single(await ResultsAsync(scenario, tenant, request))!;
        Assert.Equal("passed", item["outcome"]!.GetValue<string>());
        Assert.Equal("requests/SubmitRequest", item["name"]!.GetValue<string>());
        Assert.Equal(ScenarioFile, item["file"]!.GetValue<string>());
        Assert.Equal(["version ok", "contracts ok", "compile ok"], StepLines(result, tenant).Take(3).ToArray());
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_validate_whose_contract_scenario_fails_fails_with_AXC0342()
    {
        using var modules = ModulesWithScenario("approved");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "validate", _app);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0342", error["code"]!.GetValue<string>());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} contracts failed: AXC0342 ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_with_contract_scenarios_and_no_scenario_server_fails_with_AXC0217_and_stores_nothing()
    {
        using var modules = ModulesWithScenario("submitted");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish", _app);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: false);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0217", error["code"]!.GetValue<string>());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} contracts failed: AXC0217 ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_skips_a_contract_scenario_that_covers_a_relaxed_point_and_runs_the_others()
    {
        using var modules = ModulesWithScenario("approved");
        File.WriteAllText(
            System.IO.Path.Combine(modules.Path, "requests", "1.0.0", "scenarios", "create-draft.json"),
            """
            {
              "id": "9c3f5e17-2a64-4d8b-b0e9-6f1a8c4d2b73",
              "kind": "scenario",
              "name": "CreateDraft",
              "formatVersion": 1,
              "users": { "requester": ["requester"] },
              "contract": { "covers": ["Request/transitions/approve"] },
              "steps": [
                { "type": "createRecord", "entity": "Request", "as": "requester", "save": "request", "values": { "title": "Chairs", "amount": 5, "status": "draft" } },
                { "type": "expectRecord", "record": "request", "values": { "status": "draft" } }
              ]
            }
            """);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var relax = new JsonArray(new JsonObject { ["point"] = "Request/transitions/submit", ["reason"] = "Tests skip it." });
        var request = await QueueAsync(scenario, tenant, "publish", _app, relax);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        var status = await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
        Assert.True(status is "succeeded", string.Join('\n', result.Output));
        var items = await ResultsAsync(scenario, tenant, request);
        var skipped = Assert.Single(items, item => item!["name"]!.GetValue<string>() == "requests/SubmitRequest")!;
        Assert.Equal("skipped", skipped["outcome"]!.GetValue<string>());
        Assert.Equal("Request/transitions/submit", skipped["point"]!.GetValue<string>());
        Assert.Equal(ScenarioFile, skipped["file"]!.GetValue<string>());
        var ran = Assert.Single(items, item => item!["name"]!.GetValue<string>() == "requests/CreateDraft")!;
        Assert.Equal("passed", ran["outcome"]!.GetValue<string>());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} scenario requests/SubmitRequest skipped:", StringComparison.Ordinal));
        Assert.Equal(["version ok", "contracts ok", "compile ok"], StepLines(result, tenant).Take(3).ToArray());
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_whose_contract_scenarios_are_all_skipped_needs_no_scenario_server()
    {
        using var modules = ModulesWithScenario("approved");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var relax = new JsonArray(new JsonObject { ["point"] = "Request/transitions/submit", ["reason"] = "Tests skip it." });
        var request = await QueueAsync(scenario, tenant, "publish", _app, relax);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: false);

        var status = await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
        Assert.True(status is "succeeded", string.Join('\n', result.Output));
        var item = Assert.Single(await ResultsAsync(scenario, tenant, request))!;
        Assert.Equal("skipped", item["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_publish_of_an_application_that_declares_organization_runs_its_five_contract_scenarios_and_they_pass()
    {
        var modules = System.IO.Path.Combine(AppContext.BaseDirectory, "BuiltInModules");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish", _organizationApp);

        var result = await RunPublisherAsync(scenario, tenant, request, modules, withScenarioServer: true);

        var status = await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
        Assert.True(status is "succeeded", string.Join('\n', result.Output));
        var items = await ResultsAsync(scenario, tenant, request);
        Assert.Equal(
            [
                "organization/AdminManagesOrganization",
                "organization/DelegateNeedsDatesInOrder",
                "organization/DepartmentIsNotItsOwnParent",
                "organization/EmployeeIsNotTheirOwnManagerOrDelegate",
                "organization/MemberUpdatesOnlyTheirOwnAbsence",
            ],
            items.Select(item => item!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.All(items, item => Assert.Equal("passed", item!["outcome"]!.GetValue<string>()));
        Assert.Equal(["version ok", "contracts ok", "compile ok"], StepLines(result, tenant).Take(3).ToArray());
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_of_an_application_that_declares_approvals_runs_its_contract_scenarios_and_they_pass()
    {
        var modules = System.IO.Path.Combine(AppContext.BaseDirectory, "BuiltInModules");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish", _approvalsApp);

        var result = await RunPublisherAsync(scenario, tenant, request, modules, withScenarioServer: true);

        var status = await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
        Assert.True(status is "succeeded", string.Join('\n', result.Output));
        var items = (await ResultsAsync(scenario, tenant, request)).Where(item => item!["name"]!.GetValue<string>().StartsWith("approvals/", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                "approvals/AccessStaysWithinTheCeilings",
                "approvals/AmountIsFrozenAfterSubmit",
                "approvals/OneDecisionMovesOneStep",
                "approvals/RequestGoesToManagerThenNamedEmployee",
                "approvals/RequestMovesOnlyThroughItsLifecycle",
                "approvals/RequestTypeNeedsAStepForEveryAmount",
                "approvals/RequesterCannotDecideOwnRequest",
            ],
            items.Select(item => item!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.All(items, item => Assert.Equal("passed", item!["outcome"]!.GetValue<string>()));
        Assert.Equal(["version ok", "contracts ok", "compile ok"], StepLines(result, tenant).Take(3).ToArray());
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_against_an_approvals_copy_that_approves_right_after_the_submit_fails_naming_the_scenario_and_its_step()
    {
        using var modules = new TempModules();
        CopyFolder(System.IO.Path.Combine(AppContext.BaseDirectory, "BuiltInModules"), modules.Path);
        // The branch after the submit never takes the way to the steps, so the process goes straight to markApproved.
        var processFile = System.IO.Path.Combine(modules.Path, "approvals", "1.0.1", "processes", "request-approval.json");
        var process = JsonNode.Parse(await File.ReadAllTextAsync(processFile, CancellationToken))!;
        Assert.Equal("nextStep", process["steps"]![1]!["name"]!.GetValue<string>());
        process["steps"]![1]!["branches"]![0]!["when"] = "step < 0";
        await File.WriteAllTextAsync(processFile, process.ToJsonString(), CancellationToken);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish", _approvalsApp);

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        const string file = "modules/approvals/scenarios/request-goes-to-manager-then-named-employee.json";
        var diagnostics = await DiagnosticsAsync(scenario, tenant, request);
        var error = Assert.Single(diagnostics, diagnostic => diagnostic["code"]!.GetValue<string>() == "AXC0342" && diagnostic["file"]!.GetValue<string>() == file);
        Assert.Equal("/steps/6", error["path"]!.GetValue<string>());
        var message = error["message"]!.GetValue<string>();
        Assert.Contains("'approvals'", message, StringComparison.Ordinal);
        Assert.Contains("'RequestGoesToManagerThenNamedEmployee'", message, StringComparison.Ordinal);
        Assert.Null(await ScalarAsync(scenario, tenant, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        return tenant;
    }

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, Guid request, string modules, bool withScenarioServer)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["Modules:Path"] = modules,
            ["Scenarios:ServerConnectionString"] = withScenarioServer ? await PostgreSqlServer.ScenarioConnectionStringAsync() : null,
        };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: settings);
        try
        {
            var deadline = DateTime.UtcNow + _limit;
            while (!run.IsCompleted && DateTime.UtcNow < deadline)
            {
                var status = await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
                if (status is "succeeded" or "failed")
                {
                    break;
                }

                await Task.Delay(200, CancellationToken);
            }
        }
        finally
        {
            await stop.CancelAsync();
        }

        return await run;
    }

    // Stores a request over the runtime role, as the server will. The snapshot is every JSON file of the application folder.
    // With relax, the manifest in the snapshot gets those relax entries on its first module.
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string kind, string app, JsonArray? relax = null)
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(app, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            var file = System.IO.Path.GetRelativePath(app, path).Replace('\\', '/');
            if (relax is not null && file == "application.json")
            {
                var manifestNode = JsonNode.Parse(Encoding.UTF8.GetString(bytes))!;
                manifestNode["modules"]![0]!["relax"] = relax.DeepClone();
                bytes = Encoding.UTF8.GetBytes(manifestNode.ToJsonString());
            }

            items.Add(new JsonObject
            {
                ["file"] = file,
                ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                ["content"] = Encoding.UTF8.GetString(bytes),
            });
        }

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(app, "application.json"), CancellationToken));
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, target, status, snapshot, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, @kind, 'tenant', 'queued', @snapshot, 'author', 'Author User', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Guid.Parse(manifest.RootElement.GetProperty("id").GetString()!));
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = items.ToJsonString() });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<JsonArray> ResultsAsync(AdminScenario scenario, string tenant, Guid request) =>
        JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT scenario_results::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();

    private static async Task<List<JsonNode>> DiagnosticsAsync(AdminScenario scenario, string tenant, Guid request) =>
        [.. JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray().Select(node => node!)];

    // The step lines of the tenant, such as "version ok", without the scenario lines and the claim line.
    private static string[] StepLines(AdminResult result, string tenant) =>
        [.. result.Output
            .Where(line => line.StartsWith($"{tenant} ", StringComparison.Ordinal) && !line.StartsWith($"{tenant} scenario ", StringComparison.Ordinal) && !line.StartsWith($"{tenant} claim ", StringComparison.Ordinal))
            .Select(line => line[(tenant.Length + 1)..])];

    // The databases that the scenario role owns. The contracts step creates them as that role.
    private static async Task<long> ScenarioDatabasesAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(
            await PostgreSqlServer.AdminConnectionStringAsync(),
            "SELECT count(*) FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba WHERE r.rolname = 'axis_scenario'",
            CancellationToken))!;

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);
}
