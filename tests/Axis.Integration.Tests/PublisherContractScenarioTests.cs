using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>contracts</c> step of a publish and a validate request: the contract scenarios of the pinned module run in their own
/// databases before the compile. The <c>requests</c> test module is copied to a temporary folder with one contract scenario that
/// covers its <c>submit</c> transition, because the shared fixture is used by other tests.
/// </summary>
[Collection("Test scenario runs")]
public sealed class PublisherContractScenarioTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string ScenarioFile = "modules/requests/scenarios/submit-request.json";

    private static readonly string _app = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp");
    private static readonly string _modules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");

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
    public async Task A_publish_whose_contract_scenario_fails_fails_with_AXC0341_and_stores_nothing()
    {
        using var modules = ModulesWithScenario("approved");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish");

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0341", error["code"]!.GetValue<string>());
        Assert.Equal(ScenarioFile, error["file"]!.GetValue<string>());
        Assert.Equal("/steps/2", error["path"]!.GetValue<string>());
        var message = error["message"]!.GetValue<string>();
        Assert.Contains("'requests'", message, StringComparison.Ordinal);
        Assert.Contains("'SubmitRequest'", message, StringComparison.Ordinal);
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} contracts failed: AXC0341 ", StringComparison.Ordinal));
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
        var request = await QueueAsync(scenario, tenant, "publish");

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
    public async Task A_validate_whose_contract_scenario_fails_fails_with_AXC0341()
    {
        using var modules = ModulesWithScenario("approved");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "validate");

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: true);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0341", error["code"]!.GetValue<string>());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} contracts failed: AXC0341 ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_publish_with_contract_scenarios_and_no_scenario_server_fails_with_AXC0217_and_stores_nothing()
    {
        using var modules = ModulesWithScenario("submitted");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, "publish");

        var result = await RunPublisherAsync(scenario, tenant, request, modules.Path, withScenarioServer: false);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0217", error["code"]!.GetValue<string>());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} contracts failed: AXC0217 ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    // A copy of the test modules whose requests module has a contract scenario. It submits a draft request, and then expects the
    // given status, so "approved" makes it fail at its last step.
    private static TempModules ModulesWithScenario(string expected)
    {
        var copy = new TempModules();
        CopyFolder(_modules, copy.Path);
        var folder = System.IO.Path.Combine(copy.Path, "requests", "1.0.0", "scenarios");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            System.IO.Path.Combine(folder, "submit-request.json"),
            $$"""
            {
              "id": "6b8e1d24-7c35-4a90-b1f2-3d5e7a9c0e11",
              "kind": "scenario",
              "name": "SubmitRequest",
              "formatVersion": 1,
              "users": { "requester": ["requester"] },
              "contract": { "covers": ["Request/transitions/submit"] },
              "steps": [
                { "type": "createRecord", "entity": "Request", "as": "requester", "save": "request", "values": { "title": "Chairs", "amount": 5, "status": "draft" } },
                { "type": "updateRecord", "record": "request", "as": "requester", "values": { "status": "submitted" } },
                { "type": "expectRecord", "record": "request", "values": { "status": "{{expected}}" } }
              ]
            }
            """);
        return copy;
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file)));
        }
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

    // Stores a request over the runtime role, as the server will. The snapshot is every JSON file of the ModuleContractApp fixture.
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string kind)
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(_app, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            items.Add(new JsonObject
            {
                ["file"] = System.IO.Path.GetRelativePath(_app, path).Replace('\\', '/'),
                ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                ["content"] = Encoding.UTF8.GetString(bytes),
            });
        }

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(_app, "application.json"), CancellationToken));
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

    private sealed class TempModules : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"axis-modules-{Guid.NewGuid():N}")).FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
