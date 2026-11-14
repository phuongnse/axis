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
/// The tests use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class PublisherApplicationPackageBuildTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Manifest = """
        { "id": "tools", "version": "1.0.0", "label": "Tools", "language": "python", "adapter": "adapter.py", "lock": "requirements.txt",
          "operations": [ { "name": "sum", "type": "adapter.sum", "idempotent": true, "input": [], "output": [] } ] }
        """;

    private const string NoDependencies = "# No dependencies.\n";

    private static readonly string _missingWheel = $"./wheels/missing-1.0-py3-none-any.whl --hash=sha256:{new string('0', 64)}\n";

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_package_is_built_in_the_host_and_the_publish_succeeds_while_the_publisher_has_no_python()
    {
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await StartHostAsync(extensions.Path, trivy);
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var request = await QueueAsync(scenario, tenant, NoDependencies);

        var result = await RunPublisherAsync(scenario, tenant, request, host.Address);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal([("tools", "1.0.0")], host.Builds());
        Assert.True(trivy.Ran);
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
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

    // Stores a publish of a minimal application with the package 'tools' over the runtime role, as the server does.
    private async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string requirements)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, snapshot, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, 'publish', 'queued', @snapshot, 'author', 'Author User', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", _applicationId);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = Snapshot(requirements) });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private string Snapshot(string requirements)
    {
        (string File, string Content)[] files =
        [
            ("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "Packages{{Guid.NewGuid():N}}", "formatVersion": 1 }"""),
            ("entities/order.json", $$"""{ "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ { "name": "number", "type": "text", "required": true, "maxLength": 20 } ] }"""),
            ("packages/tools/axis-package.json", Manifest),
            ("packages/tools/adapter.py", "import sys\n"),
            ("packages/tools/requirements.txt", requirements),
        ];
        var items = new JsonArray();
        foreach (var (file, content) in files)
        {
            items.Add(new JsonObject { ["file"] = file, ["version"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), ["content"] = content });
        }

        return items.ToJsonString();
    }

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, Guid request, string host)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["Extensions:ApplicationPackages"] = "true",
            ["Extensions:Runners:Application"] = host,
            ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
        };
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
