using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Configuration.Tests;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PublisherPreviewTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static readonly string[] _steps = ["version", "compile", "activate", "seed", "indexes"];

    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_queued_preview_publish_activates_the_release_on_the_preview_tenant_only()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var (main, preview) = await CreateTenantsAsync(scenario, withPreview: true);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", preview!])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", main])).Exit);
        var previewBefore = await ScalarAsync(scenario, preview!, "SELECT release_id FROM axis.active_releases");
        var previewActivationsBefore = await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.release_activations");
        var previewAuditBefore = await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.audit_records");
        var mainBefore = await ScalarAsync(scenario, main, "SELECT release_id FROM axis.active_releases");
        var mainActivationsBefore = await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.release_activations");
        var mainAuditBefore = await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.audit_records");
        Assert.NotNull(previewBefore);
        Assert.NotNull(mainBefore);
        var request = await QueueAsync(scenario, main, await SnapshotAsync(EditedTitle), "preview");

        var result = await RunPublisherAsync(scenario, main, request);

        Assert.Equal(0, result.Exit);
        Assert.Equal($"{main} claim ok", result.Output[0]);
        Assert.Equal(_steps.Select(step => $"{preview} {step} ok"), result.Output.Skip(1));
        // The preview tenant has no purchasing system or company bindings, so activation only warns about them.
        Assert.All(result.Error, line => Assert.Contains($"{preview} warning AXC0100 ", line, StringComparison.Ordinal));
        foreach (var name in new[] { CompanyBindings.Issuer, CompanyBindings.ClientId, CompanyBindings.ClientSecret })
        {
            Assert.Contains(result.Error, line => line.Contains($"{preview} warning AXC0100 ", StringComparison.Ordinal) && line.Contains(name, StringComparison.Ordinal));
        }

        Assert.Equal("succeeded", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var previewAfter = await ScalarAsync(scenario, preview!, "SELECT release_id FROM axis.active_releases");
        Assert.NotEqual(previewBefore, previewAfter);
        Assert.Equal(previewAfter, await ScalarAsync(scenario, main, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal((long)previewActivationsBefore! + 1, await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal(request, await ScalarAsync(scenario, preview!, $"SELECT request_id FROM axis.release_activations WHERE release_id = '{previewAfter}'"));
        Assert.Equal(previewAuditBefore, await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.audit_records"));

        // Nothing of the preview reached the main tenant, and the request wrote no audit record there.
        Assert.Equal(mainBefore, await ScalarAsync(scenario, main, "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(mainActivationsBefore, await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal(mainAuditBefore, await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.audit_records"));
        Assert.Equal(1L, await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.releases"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_preview_request_fails_with_AXC0216_when_the_main_tenant_has_no_preview_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var (main, _) = await CreateTenantsAsync(scenario, withPreview: false);
        var request = await QueueAsync(scenario, main, await SnapshotAsync(null), "preview");

        var result = await RunPublisherAsync(scenario, main, request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains($"{main} preview failed: AXC0216 The tenant has no preview tenant.", result.Output);
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, main, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        var error = Assert.Single(diagnostics)!;
        Assert.Equal("AXC0216", error["code"]!.GetValue<string>());
        Assert.Equal("error", error["severity"]!.GetValue<string>());
        Assert.Equal(0L, await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.audit_records"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_preview_request_stays_queued_while_the_preview_tenant_is_suspended_and_a_later_tenant_request_runs()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var (main, preview) = await CreateTenantsAsync(scenario, withPreview: true);
        await scenario.SetStatusAsync(preview!, "suspended");
        var snapshot = await SnapshotAsync(null);
        var waiting = await QueueAsync(scenario, main, snapshot, "preview");
        var later = await QueueAsync(scenario, main, snapshot, "tenant");

        var result = await RunPublisherAsync(scenario, main, later);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{later}'"));
        Assert.Equal("queued", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{waiting}'"));
        Assert.Equal(0, await ScalarAsync(scenario, main, $"SELECT claims FROM axis.publish_requests WHERE id = '{waiting}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.releases"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_validate_with_target_preview_is_never_claimed()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var (main, preview) = await CreateTenantsAsync(scenario, withPreview: true);
        var snapshot = await SnapshotAsync(null);
        var validate = await QueueAsync(scenario, main, snapshot, "preview", kind: "validate");
        var later = await QueueAsync(scenario, main, snapshot, "tenant");

        var result = await RunPublisherAsync(scenario, main, later);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{later}'"));
        Assert.Equal("queued", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{validate}'"));
        Assert.Equal(0, await ScalarAsync(scenario, main, $"SELECT claims FROM axis.publish_requests WHERE id = '{validate}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.releases"));
    }

    [Fact]
    public async Task A_preview_publish_that_does_not_compile_fails_and_the_preview_tenant_keeps_its_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var (main, preview) = await CreateTenantsAsync(scenario, withPreview: true);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", preview!])).Exit);
        var before = await ScalarAsync(scenario, preview!, "SELECT release_id FROM axis.active_releases");
        var activationsBefore = await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.release_activations");
        Assert.NotNull(before);
        var request = await QueueAsync(scenario, main, await SnapshotAsync(BrokenTitle), "preview");

        var result = await RunPublisherAsync(scenario, main, request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, main, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Contains(result.Output, line => Regex.IsMatch(line, $@"^{preview} compile failed: AXC\d{{4}} entities/purchase-request.json "));
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, main, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        Assert.Single(diagnostics, item => item!["file"]!.GetValue<string>() == "entities/purchase-request.json" && item["severity"]!.GetValue<string>() == "error");
        Assert.Equal(before, await ScalarAsync(scenario, preview!, "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(activationsBefore, await ScalarAsync(scenario, preview!, "SELECT count(*) FROM axis.release_activations"));
        Assert.Equal(0L, await ScalarAsync(scenario, main, "SELECT count(*) FROM axis.audit_records"));
        await scenario.AssertNoSecretsAsync();
    }

    private const string EditedTitle = "title";

    private const string BrokenTitle = "broken";

    // The main tenant with the purchase request sample's bindings, and its preview tenant when asked.
    private static async Task<(string Main, string? Preview)> CreateTenantsAsync(AdminScenario scenario, bool withPreview)
    {
        var main = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, $"{main}.example.test")).Exit);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = main, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = main,
                    Name = SecretBinding,
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(main, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
                });
            context.TenantBindings.AddRange(CompanyBindings.Rows(main));
            await context.SaveChangesAsync(CancellationToken);
        }

        if (!withPreview)
        {
            return (main, null);
        }

        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, "preview.example.test"), withClientSecret: false)).Exit);
        return (main, preview);
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
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string snapshot, string target, string kind = "publish")
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, target, status, snapshot, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, @kind, @target, 'queued', @snapshot, 'author', 'Author User', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Guid.Parse(await ApplicationIdAsync()));
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("target", target);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = snapshot });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<string> ApplicationIdAsync()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_sample, "application.json"), CancellationToken));
        return manifest.RootElement.GetProperty("id").GetString()!;
    }

    // Every JSON file of the sample, as the publish route will store it. <see cref="EditedTitle"/> changes a text, so the release
    // is a new one. <see cref="BrokenTitle"/> gives the title field a type that does not exist, so the entity does not compile.
    private static async Task<string> SnapshotAsync(string? change)
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(_sample, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var file = Path.GetRelativePath(_sample, path).Replace('\\', '/');
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            var content = Encoding.UTF8.GetString(bytes);
            if (change == BrokenTitle && file == "entities/purchase-request.json")
            {
                content = Replace(content, "\"name\": \"title\", \"type\": \"text\"", "\"name\": \"title\", \"type\": \"nonsense\"");
            }
            else if (change == EditedTitle && file == "texts/en.json")
            {
                content = Replace(content, "\"purchaseRequest.title\": \"Title\"", "\"purchaseRequest.title\": \"Request title\"");
            }

            items.Add(new JsonObject { ["file"] = file, ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["content"] = content });
        }

        return items.ToJsonString();
    }

    private static string Replace(string content, string from, string to)
    {
        var changed = content.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(content, changed);
        return changed;
    }

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);
}
