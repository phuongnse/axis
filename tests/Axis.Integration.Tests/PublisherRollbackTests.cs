using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PublisherRollbackTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string NoteOrder = """{ "name": "note", "type": "text" }""";
    private const string RemarkOrder = """{ "name": "remark", "type": "text" }""";

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"Rollback{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rollback_across_a_rename_migration_fails_with_AXC0174_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order(NoteOrder));
        await ActivateAsync(scenario, tenant, folder.Path);
        folder
            .With("entities/order.json", Order(RemarkOrder))
            .With("migrations/0001-rename-note.json", RenameMigration());
        await ActivateAsync(scenario, tenant, folder.Path);
        var first = await ReleaseOfNumberAsync(scenario, tenant, 1);
        var second = await ReleaseOfNumberAsync(scenario, tenant, 2);
        Assert.Equal(second, await ActiveReleaseAsync(scenario, tenant));
        var activations = await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations");
        var request = await QueueRollbackAsync(scenario, tenant, first);

        var result = await RunPublisherAsync(scenario, tenant, request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal([$"{tenant} claim ok", $"{tenant} version ok", $"{tenant} rollback ok"], result.Output.Take(3));
        Assert.StartsWith($"{tenant} activate failed: {DiagnosticCodes.ChangedMigrationHistory} application.json : ", Assert.Single(result.Output.Skip(3)), StringComparison.Ordinal);
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        Assert.Contains(diagnostics, item => item!["code"]!.GetValue<string>() == DiagnosticCodes.ChangedMigrationHistory);
        Assert.Equal(first, await ScalarAsync(scenario, tenant, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(second, await ActiveReleaseAsync(scenario, tenant));
        Assert.Equal(activations, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        await AssertOneAuditRecordAsync(scenario, tenant, "release.rollbackFailed", request, first, 1);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_rollback_to_a_release_that_pins_a_package_version_that_is_not_installed_fails_with_AXC0146()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var older = new TemporaryFolder().Install("1.1.0");
        using var folder = Application(version: "1.0.0").With("entities/order.json", Order(NoteOrder));
        var installed = new Dictionary<string, string?> { ["Extensions:Path"] = packages.Path };
        await ActivateAsync(scenario, tenant, folder.Path, installed);
        folder.With("application.json", Manifest("1.1.0"));
        await ActivateAsync(scenario, tenant, folder.Path, installed);
        var first = await ReleaseOfNumberAsync(scenario, tenant, 1);
        var second = await ReleaseOfNumberAsync(scenario, tenant, 2);
        var activations = await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations");
        var request = await QueueRollbackAsync(scenario, tenant, first);

        var result = await RunPublisherAsync(scenario, tenant, request, new Dictionary<string, string?> { ["Extensions:Path"] = older.Path });

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} rollback failed: {DiagnosticCodes.PinnedPackageVersionMissing} application.json /packages/0/version: ", StringComparison.Ordinal));
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        var diagnostic = Assert.Single(diagnostics)!;
        Assert.Equal(DiagnosticCodes.PinnedPackageVersionMissing, diagnostic["code"]!.GetValue<string>());
        Assert.Equal(second, await ActiveReleaseAsync(scenario, tenant));
        Assert.Equal(activations, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.release_activations"));
        await AssertOneAuditRecordAsync(scenario, tenant, "release.rollbackFailed", request, first, 1);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_rollback_to_a_release_that_is_not_stored_fails_without_changing_the_active_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order(NoteOrder));
        await ActivateAsync(scenario, tenant, folder.Path);
        var active = await ActiveReleaseAsync(scenario, tenant);
        var request = await QueueRollbackAsync(scenario, tenant, Guid.NewGuid());

        var result = await RunPublisherAsync(scenario, tenant, request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} rollback failed: ", StringComparison.Ordinal));
        Assert.Equal(active, await ActiveReleaseAsync(scenario, tenant));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.rollbackFailed'"));
        Assert.Equal(0L, await ScalarAsync(scenario, tenant, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.rolledBack'"));
    }

    private static async Task AssertOneAuditRecordAsync(AdminScenario scenario, string tenant, string action, Guid request, Guid release, int number)
    {
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, $"SELECT count(*) FROM axis.audit_records WHERE action LIKE 'release.%'"));
        Assert.Equal(1L, await ScalarAsync(scenario, tenant, $"SELECT count(*) FROM axis.audit_records WHERE action = '{action}'"));
        Assert.Equal("hoa", await ScalarAsync(scenario, tenant, $"SELECT actor FROM axis.audit_records WHERE action = '{action}'"));
        var details = JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT details::text FROM axis.audit_records WHERE action = '{action}'"))!)!;
        Assert.Equal(request.ToString("D"), details["requestId"]!.GetValue<string>());
        Assert.Equal(release.ToString("D"), details["releaseId"]!.GetValue<string>());
        Assert.Equal(number, details["number"]!.GetValue<int>());
    }

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        return tenant;
    }

    private static async Task ActivateAsync(AdminScenario scenario, string tenant, string folder, IReadOnlyDictionary<string, string?>? extra = null) =>
        Assert.Equal(0, (await scenario.RunAsync(["activate", folder, "--tenant", tenant], extra: extra)).Exit);

    private static async Task<Guid> ReleaseOfNumberAsync(AdminScenario scenario, string tenant, int number) =>
        (Guid)(await ScalarAsync(scenario, tenant, $"SELECT release_id FROM axis.release_activations WHERE number = {number} ORDER BY activated_at LIMIT 1"))!;

    private static async Task<object?> ActiveReleaseAsync(AdminScenario scenario, string tenant) =>
        await ScalarAsync(scenario, tenant, "SELECT release_id FROM axis.active_releases");

    // Stores the request over the runtime role, as the server does.
    private async Task<Guid> QueueRollbackAsync(AdminScenario scenario, string tenant, Guid release)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, release_id, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, 'rollback', 'queued', @release, 'hoa', 'Hoa Author', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", _applicationId);
        command.Parameters.AddWithValue("release", release);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, Guid request, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["Tenancy:Source"] = "directory" };
        foreach (var (name, value) in extra ?? [])
        {
            settings[name] = value;
        }

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

    private TemporaryFolder Application(string? version = null) => new TemporaryFolder().With("application.json", Manifest(version));

    private string Manifest(string? version) =>
        version is null
            ? $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }"""
            : $$"""
              { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
                "packages": [{ "id": "{{TestPackages.Id}}", "version": "{{version}}" }] }
              """;

    private string Order(string field) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "required": true, "maxLength": 20 }, {{field}} ] }
        """;

    private static string RenameMigration() =>
        $$"""
        { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "RenameNote", "formatVersion": 1, "entity": "Order",
          "changes": [ { "type": "renameField", "from": "note", "to": "remark" } ] }
        """;
}
