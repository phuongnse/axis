using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class AdminPublisherTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static readonly string[] _steps = ["version", "compile", "activate", "seed"];

    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_queued_publish_makes_its_release_active_for_that_tenant_only_and_the_request_succeeds()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null));

        var result = await RunPublisherAsync(scenario, ids[0], request);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal($"{ids[0]} claim ok", result.Output[0]);
        Assert.Equal(_steps.Select(step => $"{ids[0]} {step} ok"), result.Output.Skip(1));
        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var release = await ScalarAsync(scenario, ids[0], $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'");
        Assert.NotNull(release);
        Assert.Equal(release, await ScalarAsync(scenario, ids[0], "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(1, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[1], "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[1], "SELECT count(*) FROM axis.releases"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_publish_that_does_not_compile_fails_with_the_diagnostic_and_keeps_the_active_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", ids[0]])).Exit);
        var before = await ScalarAsync(scenario, ids[0], "SELECT release_id FROM axis.active_releases");
        Assert.NotNull(before);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync("\"name\": \"title\", \"type\": \"nonsense\""));

        var result = await RunPublisherAsync(scenario, ids[0], request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains(result.Output, line => Regex.IsMatch(line, $@"^{ids[0]} compile failed: AXC\d{{4}} entities/purchase-request.json "));
        Assert.DoesNotContain(result.Output, line => line.Contains(" activate ", StringComparison.Ordinal) || line.Contains(" seed ", StringComparison.Ordinal));
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, ids[0], $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        var error = Assert.Single(diagnostics, item => item!["file"]!.GetValue<string>() == "entities/purchase-request.json" && item["severity"]!.GetValue<string>() == "error")!;
        Assert.Matches(@"^AXC\d{4}$", error["code"]!.GetValue<string>());
        Assert.Equal(before, await ScalarAsync(scenario, ids[0], "SELECT release_id FROM axis.active_releases"));
        Assert.Null(await ScalarAsync(scenario, ids[0], $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
        var details = JsonNode.Parse((string)(await ScalarAsync(scenario, ids[0], "SELECT details::text FROM axis.audit_records WHERE action = 'release.publishFailed'"))!)!;
        Assert.Null(details["releaseId"]);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_successful_publish_writes_one_audit_record_for_the_requesting_user_the_application_and_the_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null));

        Assert.Equal(0, (await RunPublisherAsync(scenario, ids[0], request)).Exit);

        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action LIKE 'release.%'"));
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
        Assert.Equal("hoa", await ScalarAsync(scenario, ids[0], "SELECT actor FROM axis.audit_records WHERE action = 'release.published'"));
        Assert.Equal("Hoa Author", await ScalarAsync(scenario, ids[0], "SELECT actor_name FROM axis.audit_records WHERE action = 'release.published'"));
        Assert.Equal(await ApplicationIdAsync(), await ScalarAsync(scenario, ids[0], "SELECT application_id::text FROM axis.audit_records WHERE action = 'release.published'"));
        var details = JsonNode.Parse((string)(await ScalarAsync(scenario, ids[0], "SELECT details::text FROM axis.audit_records WHERE action = 'release.published'"))!)!;
        Assert.Equal(request.ToString("D"), details["requestId"]!.GetValue<string>());
        var release = ((Guid)(await ScalarAsync(scenario, ids[0], "SELECT release_id FROM axis.active_releases"))!).ToString("D");
        Assert.Equal(release, details["releaseId"]!.GetValue<string>());
        Assert.Null(details["number"]);
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal("hoa", await ScalarAsync(scenario, ids[0], "SELECT actor FROM axis.release_activations"));
        Assert.Equal("Hoa Author", await ScalarAsync(scenario, ids[0], "SELECT actor_name FROM axis.release_activations"));
        Assert.Equal(request, await ScalarAsync(scenario, ids[0], "SELECT request_id FROM axis.release_activations"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_request_of_a_suspended_tenant_stays_queued_while_another_tenants_request_runs()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var snapshot = await SnapshotAsync(null);
        var served = await QueueAsync(scenario, ids[0], snapshot);
        var suspended = await QueueAsync(scenario, ids[1], snapshot);
        await scenario.SetStatusAsync(ids[1], "suspended");

        var result = await RunPublisherAsync(scenario, ids[0], served);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{served}'"));
        Assert.Equal("queued", await ScalarAsync(scenario, ids[1], $"SELECT status FROM axis.publish_requests WHERE id = '{suspended}'"));
        Assert.Equal(0, await ScalarAsync(scenario, ids[1], $"SELECT claims FROM axis.publish_requests WHERE id = '{suspended}'"));
        Assert.All(result.Output, line => Assert.StartsWith($"{ids[0]} ", line, StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[1], "SELECT count(*) FROM axis.releases"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Only_publish_requests_are_claimed_and_the_oldest_of_an_application_runs_first()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var snapshot = await SnapshotAsync(null);
        var validate = await QueueAsync(scenario, ids[0], snapshot, kind: "validate");
        var first = await QueueAsync(scenario, ids[0], snapshot);
        var second = await QueueAsync(scenario, ids[0], snapshot);

        var result = await RunPublisherAsync(scenario, ids[0], second);

        Assert.Equal(0, result.Exit);
        Assert.Equal("queued", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{validate}'"));
        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{first}'"));
        Assert.True((bool)(await ScalarAsync(
            scenario,
            ids[0],
            $"SELECT (SELECT started_at FROM axis.publish_requests WHERE id = '{first}') <= (SELECT started_at FROM axis.publish_requests WHERE id = '{second}')"))!);
        Assert.Equal(2L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
    }

    [Fact]
    public async Task Publisher_exits_0_after_a_stop_and_2_for_an_extra_argument_or_a_tenant_source_that_is_not_directory()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await CreateTenantsAsync(scenario);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: _directory);
        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken);
        Assert.False(run.IsCompleted);
        await stop.CancelAsync();
        var stopped = await run;

        var extra = await scenario.RunAsync(["publisher", "--now"], extra: _directory);
        var unset = await scenario.RunAsync(["publisher"]);
        var configuration = await scenario.RunAsync(["publisher"], extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "configuration" });
        var invalid = await scenario.RunAsync(["publisher"], extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "nowhere" });
        var refresh = await scenario.RunAsync(
            ["publisher"], extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory", ["Tenancy:RefreshInterval"] = "0" });

        Assert.Equal(0, stopped.Exit);
        Assert.Empty(stopped.Error);
        Assert.All([extra, unset, configuration, invalid, refresh], result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
        });
        Assert.StartsWith("Usage:", extra.Error[0], StringComparison.Ordinal);
        Assert.All([unset, configuration, invalid], result => Assert.Contains("Tenancy:Source", result.Error[0], StringComparison.Ordinal));
        Assert.DoesNotContain("nowhere", invalid.Error[0], StringComparison.Ordinal);
        Assert.Contains("Tenancy:RefreshInterval", refresh.Error[0], StringComparison.Ordinal);
        await scenario.AssertNoSecretsAsync();
    }

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, Guid request)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: _directory);
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

    // Stores a request over the runtime role, as the server will.
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string snapshot, string kind = "publish")
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, snapshot, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, @kind, 'queued', @snapshot, 'hoa', 'Hoa Author', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Guid.Parse(await ApplicationIdAsync()));
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = snapshot });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<string> ApplicationIdAsync()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_sample, "application.json"), CancellationToken));
        return manifest.RootElement.GetProperty("id").GetString()!;
    }

    // Every JSON file of the sample, as the publish route will store it. A title field that is replaced makes the entity not compile.
    private static async Task<string> SnapshotAsync(string? breakTitle)
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(_sample, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var file = Path.GetRelativePath(_sample, path).Replace('\\', '/');
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            var content = Encoding.UTF8.GetString(bytes);
            if (breakTitle is not null && file == "entities/purchase-request.json")
            {
                var broken = content.Replace("\"name\": \"title\", \"type\": \"text\"", breakTitle, StringComparison.Ordinal);
                Assert.NotEqual(content, broken);
                content = broken;
            }

            items.Add(new JsonObject { ["file"] = file, ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["content"] = content });
        }

        return items.ToJsonString();
    }

    // Two active tenants with the purchase request sample's bindings, in id order.
    private static async Task<string[]> CreateTenantsAsync(AdminScenario scenario)
    {
        var ids = new[] { scenario.NewTenantId(), scenario.NewTenantId() }.Order(StringComparer.Ordinal).ToArray();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        foreach (var id in ids)
        {
            Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = id, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = id,
                    Name = SecretBinding,
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
                });
        }

        await context.SaveChangesAsync(CancellationToken);
        return ids;
    }

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);
}
