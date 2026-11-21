using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Integration.Tests;

/// <summary>
/// The publisher runs a stored <c>run</c> request: it builds the package of the snapshot in the test package host, starts a runner from the built
/// files with the tenant's bindings, calls the operation and stores <c>run</c> and <c>output</c>, see
/// <c>docs/reference/package-runners.md#an-applications-packages</c>. The tests insert the row, as the route of a later part will, run the
/// publisher of tenant <c>default</c> on a real PostgreSQL database against <c>TestPackageHost</c> with a stub Trivy, and read the row back.
/// The package is the Python starter of Studio, and the publisher's <c>PackageBuild:Python</c> names a missing command. The tests use a stub shell
/// script for Trivy, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class PublisherRunRequestTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Secret = "tenant-api-key-3f9a7c";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_of_the_starters_echo_ends_succeeded_with_the_input_as_output_a_duration_and_the_hosts_lines()
    {
        var connectionString = await NewTenantDatabaseAsync();
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var request = await QueueAsync(connectionString, StarterFiles(), "echo", """{"text":"hello"}""");

        await using var publisher = RunPublisher(connectionString, host);
        await WaitForFinalAsync(connectionString, request);

        Assert.Equal("succeeded", await ColumnAsync(connectionString, request, "status"));
        Assert.Equal("[]", await ColumnAsync(connectionString, request, "diagnostics"));
        var run = JsonNode.Parse((await ColumnAsync(connectionString, request, "run"))!)!.AsObject();
        Assert.Equal(["durationMs", "error", "operation", "outcome", "output"], run.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("echo", run["operation"]!.GetValue<string>());
        Assert.Equal("succeeded", run["outcome"]!.GetValue<string>());
        Assert.Equal("hello", run["output"]!["text"]!.GetValue<string>());
        Assert.Null(run["error"]);
        Assert.True(run["durationMs"]!.GetValue<long>() >= 0);
        var lines = JsonNode.Parse((await ColumnAsync(connectionString, request, "output"))!)!.AsArray();
        Assert.NotEmpty(lines);
        Assert.Single(host.TenantRunnerIds("default", "tools", "0.1.0"));
        Assert.Equal(0L, await CountAsync(connectionString, "axis.releases"));
    }

    [Fact]
    public async Task An_operation_that_raises_ends_failed_with_the_error_and_one_the_package_does_not_declare_ends_failed_with_code_operation()
    {
        var connectionString = await NewTenantDatabaseAsync();
        var files = StarterFiles();
        files["adapter.py"] += "\n\ndef fail(input, context):\n    raise ValueError(\"not for the log\")\n";
        files["axis-package.json"] = WithOperation(files["axis-package.json"], """{ "name": "fail", "type": "fail", "idempotent": true, "input": [], "output": [] }""");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var raised = await QueueAsync(connectionString, files, "fail", "{}");
        var unknown = await QueueAsync(connectionString, files, "nope", "{}");

        await using var publisher = RunPublisher(connectionString, host);
        await WaitForFinalAsync(connectionString, raised);
        await WaitForFinalAsync(connectionString, unknown);

        Assert.Equal("failed", await ColumnAsync(connectionString, raised, "status"));
        var run = JsonNode.Parse((await ColumnAsync(connectionString, raised, "run"))!)!.AsObject();
        Assert.Equal("fail", run["operation"]!.GetValue<string>());
        Assert.Equal("failed", run["outcome"]!.GetValue<string>());
        Assert.Null(run["output"]);
        var error = run["error"]!.AsObject();
        Assert.Contains("ValueError", error["code"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal($"The operation raised {error["code"]!.GetValue<string>()}.", error["message"]!.GetValue<string>());

        // The protocol carries no exception message, so none is stored.
        Assert.DoesNotContain("not for the log", (await ColumnAsync(connectionString, raised, "run"))!, StringComparison.Ordinal);
        Assert.Equal("failed", await ColumnAsync(connectionString, unknown, "status"));
        Assert.Equal("[]", await ColumnAsync(connectionString, unknown, "diagnostics"));
        var missing = JsonNode.Parse((await ColumnAsync(connectionString, unknown, "run"))!)!.AsObject();
        Assert.Equal("nope", missing["operation"]!.GetValue<string>());
        Assert.Equal("failed", missing["outcome"]!.GetValue<string>());
        Assert.Equal("operation", missing["error"]!["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(missing["error"]!["message"]!.GetValue<string>()));
    }

    [Fact]
    public async Task An_operation_that_declares_a_secret_capability_returns_the_tenants_binding_value()
    {
        var connectionString = await NewTenantDatabaseAsync();
        var files = StarterFiles();
        files["adapter.py"] += "\n\ndef token(input, context):\n    return {\"text\": context.get_secret(\"ApiKey\")}\n";
        files["axis-package.json"] = WithOperation(
            files["axis-package.json"],
            """{ "name": "token", "type": "token", "idempotent": true, "input": [], "output": [{ "name": "text", "type": "text", "required": true }], "capabilities": { "secrets": ["ApiKey"] } }""");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var request = await QueueAsync(connectionString, files, "token", "{}");

        await using var publisher = RunPublisher(connectionString, host);
        await WaitForFinalAsync(connectionString, request);

        Assert.Equal("succeeded", await ColumnAsync(connectionString, request, "status"));
        var run = JsonNode.Parse((await ColumnAsync(connectionString, request, "run"))!)!.AsObject();
        Assert.Equal("succeeded", run["outcome"]!.GetValue<string>());
        Assert.Equal(Secret, run["output"]!["text"]!.GetValue<string>());
        Assert.DoesNotContain(Secret, (await ColumnAsync(connectionString, request, "output"))!, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, (await ColumnAsync(connectionString, request, "diagnostics"))!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_operation_whose_declared_secret_the_tenant_lacks_ends_failed_with_code_binding_and_runs_nothing()
    {
        var connectionString = await NewTenantDatabaseAsync();
        var files = StarterFiles();
        files["adapter.py"] += "\n\ndef token(input, context):\n    return {\"text\": context.get_secret(\"Missing\")}\n";
        files["axis-package.json"] = WithOperation(
            files["axis-package.json"],
            """{ "name": "token", "type": "token", "idempotent": true, "input": [], "output": [{ "name": "text", "type": "text", "required": true }], "capabilities": { "secrets": ["Missing"] } }""");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var request = await QueueAsync(connectionString, files, "token", "{}");

        await using var publisher = RunPublisher(connectionString, host);
        await WaitForFinalAsync(connectionString, request);

        Assert.Equal("failed", await ColumnAsync(connectionString, request, "status"));
        var run = JsonNode.Parse((await ColumnAsync(connectionString, request, "run"))!)!.AsObject();
        Assert.Equal("binding", run["error"]!["code"]!.GetValue<string>());
        Assert.Contains("'Missing'", run["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(host.TenantRunnerIds("default", "tools", "0.1.0"));
    }

    [Fact]
    public async Task A_package_whose_adapter_has_a_syntax_error_ends_failed_with_AXC0149_at_its_file_and_no_run()
    {
        var connectionString = await NewTenantDatabaseAsync();
        var files = StarterFiles();
        files["adapter.py"] = "def broken(:\n";
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var request = await QueueAsync(connectionString, files, "echo", """{"text":"hello"}""");

        await using var publisher = RunPublisher(connectionString, host);
        await WaitForFinalAsync(connectionString, request);

        Assert.Equal("failed", await ColumnAsync(connectionString, request, "status"));
        Assert.Null(await ColumnAsync(connectionString, request, "run"));
        var diagnostic = Assert.Single(JsonNode.Parse((await ColumnAsync(connectionString, request, "diagnostics"))!)!.AsArray())!;
        Assert.Equal("AXC0149", diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/adapter.py", diagnostic["file"]!.GetValue<string>());
        Assert.NotEmpty(JsonNode.Parse((await ColumnAsync(connectionString, request, "output"))!)!.AsArray());
        Assert.Empty(host.TenantRunnerIds("default", "tools", "0.1.0"));
    }

    [Fact]
    public async Task A_run_with_the_application_packages_setting_off_ends_failed_with_AXC0148_sends_nothing_and_has_no_run()
    {
        var connectionString = await NewTenantDatabaseAsync();
        var request = await QueueAsync(connectionString, StarterFiles(), "echo", """{"text":"hello"}""");

        await using var publisher = StudioPublishTests.RunPublisher("default", "localhost", connectionString);
        await WaitForFinalAsync(connectionString, request);

        Assert.Equal("failed", await ColumnAsync(connectionString, request, "status"));
        Assert.Null(await ColumnAsync(connectionString, request, "run"));
        var diagnostic = Assert.Single(JsonNode.Parse((await ColumnAsync(connectionString, request, "diagnostics"))!)!.AsArray())!;
        Assert.Equal("AXC0148", diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/axis-package.json", diagnostic["file"]!.GetValue<string>());
    }

    // The publisher of tenant default with application packages on, the host of the test as its application runner, a free port for its egress
    // proxy and the secret binding ApiKey.
    private static StudioPublishTests.RunningPublisher RunPublisher(string connectionString, TestPackageHost host) =>
        StudioPublishTests.RunPublisher(
            "default",
            "localhost",
            connectionString,
            extra: new Dictionary<string, string?>
            {
                ["Extensions:ApplicationPackages"] = "true",
                ["Extensions:Runners:Application"] = host.Address,
                ["Extensions:Runners:ProxyPort"] = "0",
                ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
                ["Tenants:default:Bindings:ApiKey:Kind"] = "secret",
                ["Tenants:default:Bindings:ApiKey:Value"] = Secret,
            });

    // A migrated database of its own for the tenant, so no row of another test is claimed.
    private async Task<string> NewTenantDatabaseAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("publisher_run", CancellationToken);
        await StudioPackageBuildTests.MigrateTenantDatabaseAsync(connectionString);
        return connectionString;
    }

    // The Python starter of Studio, filled in as a package tools: its file names and texts.
    private static Dictionary<string, string> StarterFiles()
    {
        var starters = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Starters", "packageStarters.json")))!.AsObject();
        return starters["python"]!.AsObject().ToDictionary(
            file => file.Key,
            file => file.Value!.GetValue<string>().Replace("$id", "tools", StringComparison.Ordinal).Replace("$label", "Tools", StringComparison.Ordinal),
            StringComparer.Ordinal);
    }

    private static string WithOperation(string manifest, string operation)
    {
        var node = JsonNode.Parse(manifest)!.AsObject();
        node["operations"]!.AsArray().Add(JsonNode.Parse(operation));
        return node.ToJsonString();
    }

    // Stores a queued run request as the route of a later part will: the files of packages/tools in the snapshot, the package, the operation and the input.
    private static async Task<Guid> QueueAsync(string connectionString, Dictionary<string, string> files, string operation, string input)
    {
        var snapshot = new JsonArray([.. files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => (JsonNode)new JsonObject
        {
            ["file"] = $"packages/tools/{file.Key}",
            ["version"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Value))),
            ["content"] = file.Value,
        })]);
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, target, status, snapshot, requested_by, requested_at, package, operation, input)
            VALUES (@id, @application, 'run', 'tenant', 'queued', @snapshot, 'developer', clock_timestamp(), 'tools', @operation, @input)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Guid.NewGuid());
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlDbType.Jsonb) { Value = snapshot.ToJsonString() });
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = input });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    // Polls the row until it is succeeded or failed.
    private static async Task WaitForFinalAsync(string connectionString, Guid request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            if (await ColumnAsync(connectionString, request, "status") is "succeeded" or "failed")
            {
                return;
            }

            await Task.Delay(200, CancellationToken);
        }

        Assert.Fail("The request did not end in time. Is the publisher running?");
    }

    private static async Task<string?> ColumnAsync(string connectionString, Guid request, string column) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT {column}::text FROM axis.publish_requests WHERE id = '{request}'", CancellationToken);

    private static async Task<long> CountAsync(string connectionString, string table) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT count(*) FROM {table}", CancellationToken))!;
}
