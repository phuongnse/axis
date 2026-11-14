using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// With <c>Extensions:ApplicationPackages</c> on, a <c>publish</c> sends each package of its snapshot to the package host as a
/// <c>build</c> before it compiles, see docs/reference/extension-packages.md#an-applications-packages. The host of this process
/// runs the build, with a stub Trivy and the real pip, while the publisher's <c>PackageBuild:Python</c> names a missing command,
/// so the publisher itself installs nothing. A refused build and a host that gives no answer each fail the publish at the
/// <c>compile</c> step, store no release and write one <c>release.publishFailed</c>. Each wait ends on the request's status.
/// A <c>test</c> that names a package sends only that package to the host as a <c>test</c> and stores each test's outcome.
/// The tests use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class PublisherApplicationPackageBuildTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Manifest = """
        { "id": "tools", "version": "1.0.0", "label": "Tools", "language": "python", "adapter": "adapter.py", "lock": "requirements.txt",
          "operations": [ { "name": "sum", "type": "adapter.sum", "idempotent": true, "input": [], "output": [] } ] }
        """;

    private const string CustomerManifest = """
        { "id": "tools", "version": "1.0.0", "label": "Tools", "language": "python", "runner": "customer", "adapter": "adapter.py", "lock": "requirements.txt",
          "operations": [ { "name": "sum", "type": "adapter.sum", "idempotent": true, "input": [], "output": [] } ] }
        """;

    private const string NoDependencies = "# No dependencies.\n";

    private const string ToolsTests = """
        import unittest


        class ToolsTests(unittest.TestCase):
            def test_passes(self):
                self.assertEqual(2, 1 + 1)

            def test_fails(self):
                self.assertEqual(1, 2)
        """;

    private const string PassingTests = """
        import unittest


        class ToolsTests(unittest.TestCase):
            def test_passes(self):
                self.assertEqual(2, 1 + 1)
        """;

    private static readonly string _missingWheel = $"./wheels/missing-1.0-py3-none-any.whl --hash=sha256:{new string('0', 64)}\n";

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_package_is_built_in_the_host_and_the_publish_succeeds_and_stores_its_built_files_while_the_publisher_has_no_python()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, PhoneNumbersWheel.Requirement());

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal([("tools", "1.0.0")], host.Builds());
        Assert.True(trivy.Ran);
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_package_files WHERE path = 'packages/tools/phonenumbers/__init__.py'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_resources WHERE path LIKE 'packages/tools/phonenumbers/%'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_changed_package_gets_a_new_release_and_the_first_release_keeps_its_built_files()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        const string firstFiles =
            "SELECT string_agg(path || ' ' || encode(sha256(content), 'hex'), E'\n' ORDER BY path) FROM axis.release_package_files " +
            "WHERE release_id = (SELECT id FROM axis.releases ORDER BY created_at, id LIMIT 1)";
        var first = await QueueAsync(scenario, tenant, NoDependencies);
        Assert.Equal(0, (await RunPublisherAsync(scenario, tenant, first, host.Address)).Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{first}'"));
        var before = (string)(await ScalarAsync(scenario, tenant, firstFiles))!;
        Assert.Contains("packages/tools/adapter.py ", before, StringComparison.Ordinal);

        var second = await QueueAsync(scenario, tenant, NoDependencies, adapter: "import os\n");
        Assert.Equal(0, (await RunPublisherAsync(scenario, tenant, second, host.Address)).Exit);

        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{second}'"));
        Assert.Equal(2L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(before, await ScalarAsync(scenario, tenant, firstFiles));
        Assert.Equal(
            2L,
            await ScalarAsync(scenario, tenant, "SELECT count(DISTINCT content) FROM axis.release_package_files WHERE path = 'packages/tools/adapter.py'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_scan_that_reports_a_high_finding_fails_the_publish_with_AXC0149_at_the_lock_file()
    {
        using var trivy = new StubTrivy(StubTrivy.HighFinding);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies);

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        await AssertRefusedAsync(scenario, tenant, request, result, DiagnosticCodes.ApplicationPackageBuildRefused);
        Assert.True(trivy.Ran);
        Assert.Empty(host.Builds());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_pip_install_that_fails_fails_the_publish_with_AXC0149_at_the_lock_file_and_nothing_is_scanned()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, _missingWheel);

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        await AssertRefusedAsync(scenario, tenant, request, result, DiagnosticCodes.ApplicationPackageBuildRefused);
        Assert.False(trivy.Ran);
        Assert.Empty(host.Builds());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_host_that_does_not_answer_fails_the_publish_with_AXC0152_at_the_manifest_without_its_address()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies);
        var address = TestPackageHost.ClosedAddress();

        var result = await RunPublisherAsync(scenario, tenant, request, address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(DiagnosticCodes.ApplicationPackageBuildNoAnswer, diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/axis-package.json", diagnostic["file"]!.GetValue<string>());
        Assert.DoesNotContain(address, diagnostic["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} compile failed: {DiagnosticCodes.ApplicationPackageBuildNoAnswer}", StringComparison.Ordinal));
        await AssertNothingStoredAsync(scenario, tenant);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_customer_hosted_package_is_not_sent_to_the_host_so_a_host_that_does_not_answer_does_not_fail_the_publish()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies, customerHosted: true);

        var result = await RunPublisherAsync(scenario, tenant, request, TestPackageHost.ClosedAddress());

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_package_test_with_a_passing_and_a_failing_test_fails_and_stores_both_outcomes()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies, package: "tools", testFile: ToolsTests);

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var tests = await PackageTestsAsync(scenario, tenant, request);
        Assert.Equal(2, tests.Count);
        var passed = Assert.Single(tests, test => test!["name"]!.GetValue<string>() == "test_tools.ToolsTests.test_passes")!;
        Assert.Equal("passed", passed["outcome"]!.GetValue<string>());
        Assert.Null(passed["message"]);
        var failed = Assert.Single(tests, test => test!["name"]!.GetValue<string>() == "test_tools.ToolsTests.test_fails")!;
        Assert.Equal("failed", failed["outcome"]!.GetValue<string>());
        Assert.StartsWith("AssertionError", failed["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(await DiagnosticsAsync(scenario, tenant, request));
        Assert.Contains(result.Output, line => line == $"{tenant} package failed: 1 of 2 tests failed.");
        Assert.Equal([("tools", "1.0.0")], host.Tests());
        Assert.Empty(host.Builds());
        Assert.Null(await ScalarAsync(scenario, tenant, $"SELECT scenario_results FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_package_test_whose_tests_all_pass_succeeds_with_no_diagnostics_and_no_audit_record()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies, package: "tools", testFile: PassingTests);

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var test = Assert.Single(await PackageTestsAsync(scenario, tenant, request))!;
        Assert.Equal("passed", test["outcome"]!.GetValue<string>());
        Assert.Empty(await DiagnosticsAsync(scenario, tenant, request));
        Assert.Contains(result.Output, line => line == $"{tenant} package ok");
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_package_test_whose_install_fails_fails_with_AXC0149_at_the_lock_file_and_stores_no_test()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, _missingWheel, package: "tools", testFile: PassingTests);

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(DiagnosticCodes.ApplicationPackageBuildRefused, diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/requirements.txt", diagnostic["file"]!.GetValue<string>());
        Assert.Empty(await PackageTestsAsync(scenario, tenant, request));
        Assert.Empty(host.Tests());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} package failed: {DiagnosticCodes.ApplicationPackageBuildRefused} ", StringComparison.Ordinal));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_package_test_without_the_setting_fails_with_AXC0148_at_the_manifest_and_sends_nothing_to_the_host()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies, package: "tools", testFile: PassingTests);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var address = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");

        var result = await RunPublisherAsync(scenario, tenant, request, address, applicationPackages: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(DiagnosticCodes.ApplicationPackagesOff, diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/axis-package.json", diagnostic["file"]!.GetValue<string>());
        Assert.Empty(await PackageTestsAsync(scenario, tenant, request));
        Assert.False(listener.Pending());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_stop_of_the_publisher_ends_a_package_test_at_once_with_AXC0220_while_the_host_holds_the_connection()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies, package: "tools", testFile: PassingTests);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var address = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: PublisherSettings(address, applicationPackages: true));
        AdminResult result;
        try
        {
            using var accepted = await listener.AcceptTcpClientAsync(CancellationToken);
            await stop.CancelAsync();
            result = await run;
        }
        finally
        {
            await stop.CancelAsync();
        }

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(DiagnosticCodes.ScenarioStopped, diagnostic["code"]!.GetValue<string>());
        Assert.Empty(await PackageTestsAsync(scenario, tenant, request));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_package_test_whose_host_does_not_answer_fails_with_AXC0152_at_the_manifest_and_stores_no_test()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies, package: "tools", testFile: PassingTests);
        var address = TestPackageHost.ClosedAddress();

        var result = await RunPublisherAsync(scenario, tenant, request, address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(DiagnosticCodes.ApplicationPackageBuildNoAnswer, diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/axis-package.json", diagnostic["file"]!.GetValue<string>());
        Assert.DoesNotContain(address, diagnostic["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(await PackageTestsAsync(scenario, tenant, request));
        await scenario.AssertNoSecretsAsync();
    }

    // The request failed with one diagnostic at the package's lock file, and nothing was stored.
    private static async Task AssertRefusedAsync(AdminScenario scenario, string tenant, Guid request, AdminResult result, string code)
    {
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(code, diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/requirements.txt", diagnostic["file"]!.GetValue<string>());
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} compile failed: {code}", StringComparison.Ordinal));
        await AssertNothingStoredAsync(scenario, tenant);
    }

    private static async Task AssertNothingStoredAsync(AdminScenario scenario, string tenant)
    {
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
    }

    private static async Task<JsonArray> PackageTestsAsync(AdminScenario scenario, string tenant, Guid request) =>
        JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT package_tests::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();

    private static async Task<JsonArray> DiagnosticsAsync(AdminScenario scenario, string tenant, Guid request) =>
        JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();

    // The host's Python is the real one. Only Trivy is the stub.
    private static Task<TestPackageHost> StartHostAsync(string extensions, StubTrivy trivy) =>
        TestPackageHost.StartAsync(extensions, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        return tenant;
    }

    // Stores a publish of a minimal application with the package 'tools' over the runtime role, as the server does. With
    // package set, it stores a test of that package instead, and testFile is the package's test file.
    private async Task<Guid> QueueAsync(
        AdminScenario scenario,
        string tenant,
        string requirements,
        bool customerHosted = false,
        string adapter = "import sys\n",
        string? package = null,
        string? testFile = null)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, snapshot, requested_by, requested_by_name, requested_at, package)
            VALUES (@id, @application, @kind, 'queued', @snapshot, 'author', 'Author User', clock_timestamp(), @package)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", _applicationId);
        command.Parameters.AddWithValue("kind", package is null ? "publish" : "test");
        command.Parameters.Add(new NpgsqlParameter("package", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)package ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = Snapshot(requirements, customerHosted, adapter, testFile) });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private string Snapshot(string requirements, bool customerHosted, string adapter, string? testFile)
    {
        var package = customerHosted
            ? new (string, string)[] { ("packages/tools/axis-package.json", CustomerManifest) }
            :
            [
                ("packages/tools/axis-package.json", Manifest),
                ("packages/tools/adapter.py", adapter),
                ("packages/tools/requirements.txt", requirements),
                .. testFile is null ? [] : new (string, string)[] { ("packages/tools/test_tools.py", testFile) },
            ];
        (string File, string Content)[] files =
        [
            ("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "Packages{{Guid.NewGuid():N}}", "formatVersion": 1 }"""),
            ("entities/order.json", $$"""{ "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ { "name": "number", "type": "text", "required": true, "maxLength": 20 } ] }"""),
            .. package,
        ];
        var items = new JsonArray();
        foreach (var (file, content) in files)
        {
            items.Add(new JsonObject { ["file"] = file, ["version"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), ["content"] = content });
        }

        return items.ToJsonString();
    }

    private static Dictionary<string, string?> PublisherSettings(string host, bool applicationPackages) => new()
    {
        ["Tenancy:Source"] = "directory",
        ["Extensions:ApplicationPackages"] = applicationPackages ? "true" : "false",
        ["Extensions:Runners:Application"] = host,
        ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
    };

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, Guid request, string host, bool applicationPackages = true)
    {
        var settings = PublisherSettings(host, applicationPackages);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: settings);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
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

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);
}
