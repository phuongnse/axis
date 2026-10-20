using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PublisherTestRequestTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";

    // The scenario that Failing is copied from, and the wrong status that makes the copy fail at its last step.
    private const string Passing = "ApprovalBelowThreshold";
    private const string FailingFile = "scenarios/failing.json";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    // A sample has several scenarios, and each one creates, migrates and drops a database.
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(180);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_test_request_of_the_purchase_request_sample_passes_every_scenario_and_leaves_no_database()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "test");

        var result = await RunPublisherAsync(scenario, tenant, request, withScenarioServer: true);

        Assert.Equal(0, result.Exit);
        var status = (string?)await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
        Assert.True(status == "succeeded", string.Join('\n', result.Output));
        var results = await ResultsAsync(scenario, tenant, request);
        var names = Directory.EnumerateFiles(Path.Combine(_sample, "scenarios"), "*.json")
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(names.Length > 1);
        Assert.Equal(names.Length, results.Count);
        Assert.All(results, item =>
        {
            Assert.Equal("passed", item!["outcome"]!.GetValue<string>());
            Assert.Null(item["step"]);
            Assert.Null(item["path"]);
            Assert.Null(item["message"]);
            Assert.True(item["durationMs"]!.GetValue<long>() >= 0);
            Assert.StartsWith("scenarios/", item["file"]!.GetValue<string>());
            Assert.True(Guid.TryParseExact(item["resourceId"]!.GetValue<string>(), "D", out _));
        });
        Assert.Equal(names, results.Select(item => item!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["version ok", "test ok"], StepLines(result, tenant));
        Assert.All(names, name => Assert.Contains($"{tenant} scenario {name} passed", result.Output));
        Assert.Equal(0L, await ScenarioDatabasesAsync());

        // A test has no release id and writes no audit record.
        Assert.Null(await ScalarAsync(scenario, tenant, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_failing_scenario_fails_the_request_with_its_step_path_and_message_and_drops_its_database()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: true), "test", scenarioNames: """["Failing"]""");

        var result = await RunPublisherAsync(scenario, tenant, request, withScenarioServer: true);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var item = Assert.Single(await ResultsAsync(scenario, tenant, request))!;
        Assert.Equal("Failing", item["name"]!.GetValue<string>());
        Assert.Equal(FailingFile, item["file"]!.GetValue<string>());
        Assert.Equal("failed", item["outcome"]!.GetValue<string>());
        // The failing step is the last of six, the expectRecord.
        Assert.Equal(5, item["step"]!.GetValue<int>());
        Assert.Equal("/steps/5", item["path"]!.GetValue<string>());
        Assert.Contains("rejected", item["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} scenario Failing failed: Step 6 (expectRecord): ", StringComparison.Ordinal));
        Assert.Contains($"{tenant} test failed: 1 of 1 scenarios failed.", result.Output);

        // A scenario failure is in the results only, not in the diagnostics.
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic!["severity"]!.GetValue<string>() == "error");
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_test_request_leaves_no_record_or_process_instance_in_the_tenant_database_and_the_server_roles_cannot_create_databases()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "test", scenarioNames: $"""["{Passing}"]""");

        var result = await RunPublisherAsync(scenario, tenant, request, withScenarioServer: true);

        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains($"{tenant} scenario {Passing} passed", result.Output);
        Assert.Equal(
            0L,
            await ScalarAsync(scenario, tenant, "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema', 'axis')"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.provisioned_entities"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.process_work_items"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.events"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));

        var admin = await PostgreSqlServer.AdminConnectionStringAsync();
        Assert.Equal(
            "axis_schema:False:False|axis_runtime:False:False|axis_scenario:True:False",
            string.Join('|', (await Task.WhenAll(
                new[] { PostgreSqlServer.SchemaRole, PostgreSqlServer.RuntimeRole, PostgreSqlServer.ScenarioRole }.Select(async role =>
                    $"{role}:{await PostgreSqlServer.AdminScalarAsync(admin, $"SELECT rolcreatedb FROM pg_roles WHERE rolname = '{role}'", CancellationToken)}:{await PostgreSqlServer.AdminScalarAsync(admin, $"SELECT rolsuper FROM pg_roles WHERE rolname = '{role}'", CancellationToken)}")))));
    }

    [Fact]
    public async Task Without_the_scenario_server_setting_a_test_request_fails_with_AXC0217_and_runs_no_scenario()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "test");

        var result = await RunPublisherAsync(scenario, tenant, request, withScenarioServer: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request));
        Assert.Equal("AXC0217", error["code"]!.GetValue<string>());
        Assert.Equal("error", error["severity"]!.GetValue<string>());
        Assert.Contains("Scenarios:ServerConnectionString", error["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("", error["file"]!.GetValue<string>());
        Assert.Equal("", error["path"]!.GetValue<string>());
        Assert.Empty(await ResultsAsync(scenario, tenant, request));
        Assert.DoesNotContain(result.Output, line => line.StartsWith($"{tenant} scenario ", StringComparison.Ordinal));
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} test failed: AXC0217 ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task An_unknown_scenario_name_fails_the_test_request_with_AXC0218_and_runs_no_scenario()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "test", scenarioNames: $"""["{Passing}", "NoSuchScenario"]""");

        var result = await RunPublisherAsync(scenario, tenant, request, withScenarioServer: true);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var error = Assert.Single(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal("AXC0218", error["code"]!.GetValue<string>());
        Assert.Equal("The scenario 'NoSuchScenario' is not in the application.", error["message"]!.GetValue<string>());
        Assert.Empty(await ResultsAsync(scenario, tenant, request));
        Assert.DoesNotContain(result.Output, line => line.StartsWith($"{tenant} scenario ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_snapshot_that_does_not_compile_fails_the_test_request_with_its_diagnostics_and_runs_no_scenario()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false, broken: true), "test");

        var result = await RunPublisherAsync(scenario, tenant, request, withScenarioServer: true);

        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains(await DiagnosticsAsync(scenario, tenant, request), diagnostic => diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Empty(await ResultsAsync(scenario, tenant, request));
        Assert.DoesNotContain(result.Output, line => line.StartsWith($"{tenant} scenario ", StringComparison.Ordinal));
        Assert.Equal(0L, await ScenarioDatabasesAsync());
    }

    [Fact]
    public async Task A_queued_test_is_claimed_and_finishes_while_a_publish_of_the_application_is_running()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var publish = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "publish");
        await SetRunningAsync(scenario, tenant, publish);
        var test = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "test", scenarioNames: $"""["{Passing}"]""");

        await RunPublisherAsync(scenario, tenant, test, withScenarioServer: true);

        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{test}'"));
        Assert.Equal("running|other-publisher|1", await ScalarAsync(scenario, tenant, $"SELECT status || '|' || claimed_by || '|' || claims FROM axis.publish_requests WHERE id = '{publish}'"));
    }

    [Fact]
    public async Task A_queued_publish_is_claimed_while_a_test_of_the_application_is_running()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var test = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "test");
        await SetRunningAsync(scenario, tenant, test);
        var publish = await QueueAsync(scenario, tenant, await SnapshotAsync(failing: false), "publish");

        await RunPublisherAsync(scenario, tenant, publish, withScenarioServer: true);

        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{publish}'"));
        Assert.Equal("running|other-publisher|1", await ScalarAsync(scenario, tenant, $"SELECT status || '|' || claimed_by || '|' || claims FROM axis.publish_requests WHERE id = '{test}'"));
    }

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        context.TenantBindings.AddRange(
            new TenantBindingRow { TenantId = tenant, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
            new TenantBindingRow
            {
                TenantId = tenant,
                Name = SecretBinding,
                Kind = "secret",
                SecretValue = AdminScenario.Cipher.Encrypt(tenant, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
            });
        await context.SaveChangesAsync(CancellationToken);
        return tenant;
    }

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, Guid request, bool withScenarioServer)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
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

    // Stores a request over the runtime role, as the server will.
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string snapshot, string kind, string? scenarioNames = null)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, target, status, snapshot, requested_by, requested_by_name, requested_at, scenario_names)
            VALUES (@id, @application, @kind, 'tenant', 'queued', @snapshot, 'hoa', 'Hoa Author', clock_timestamp(), @scenarios)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Guid.Parse(await ApplicationIdAsync()));
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = snapshot });
        command.Parameters.Add(new NpgsqlParameter("scenarios", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = (object?)scenarioNames ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    // Makes the row look as if another publisher runs it, with a lease that will not run out during the test.
    private static async Task SetRunningAsync(AdminScenario scenario, string tenant, Guid request) =>
        await ScalarAsync(
            scenario,
            tenant,
            $"UPDATE axis.publish_requests SET status = 'running', claimed_by = 'other-publisher', claims = 1, started_at = now(), lease_until = now() + interval '1 hour' WHERE id = '{request}'");

    private static async Task<string> ApplicationIdAsync()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_sample, "application.json"), CancellationToken));
        return manifest.RootElement.GetProperty("id").GetString()!;
    }

    // Every JSON file of the sample, as the test route stores it. With failing, a copy of the passing scenario expects a status
    // that the record never gets. With broken, the title field gets a type that does not exist, so the entity does not compile.
    private static async Task<string> SnapshotAsync(bool failing, bool broken = false)
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(_sample, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var file = Path.GetRelativePath(_sample, path).Replace('\\', '/');
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            var content = Encoding.UTF8.GetString(bytes);
            if (broken && file == "entities/purchase-request.json")
            {
                content = Replace(content, "\"name\": \"title\", \"type\": \"text\"", "\"name\": \"title\", \"type\": \"nonsense\"");
            }

            items.Add(Item(file, bytes, content));
        }

        if (failing)
        {
            var original = await File.ReadAllTextAsync(Path.Combine(_sample, "scenarios", "approval-below-threshold.json"), CancellationToken);
            var content = Replace(Replace(Replace(original, "09d75317-fd9e-411e-8322-317778fc1b2b", "7a1c2f0e-5d3b-4b8e-9c61-0f2d4e8a6b13"), $"\"{Passing}\"", "\"Failing\""), "\"status\": \"approved\"", "\"status\": \"rejected\"");
            items.Add(Item(FailingFile, Encoding.UTF8.GetBytes(content), content));
        }

        return items.ToJsonString();
    }

    private static JsonObject Item(string file, byte[] bytes, string content) =>
        new() { ["file"] = file, ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["content"] = content };

    private static string Replace(string content, string from, string to)
    {
        var changed = content.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(content, changed);
        return changed;
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

    // The databases that the scenario role owns. A test run creates them as that role, and the other runner tests create theirs as the superuser.
    private static async Task<long> ScenarioDatabasesAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(
            await PostgreSqlServer.AdminConnectionStringAsync(),
            "SELECT count(*) FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba WHERE r.rolname = 'axis_scenario'",
            CancellationToken))!;

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);
}
