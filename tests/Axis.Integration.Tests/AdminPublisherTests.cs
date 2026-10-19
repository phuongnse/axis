using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Admin;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        Assert.Equal(1, details["number"]!.GetValue<int>());
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
    public async Task Rollback_requests_are_not_claimed_and_the_oldest_of_an_application_runs_first()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var snapshot = await SnapshotAsync(null);
        var rollback = await QueueAsync(scenario, ids[0], snapshot, kind: "rollback");
        var first = await QueueAsync(scenario, ids[0], snapshot);
        var second = await QueueAsync(scenario, ids[0], snapshot);

        var result = await RunPublisherAsync(scenario, ids[0], second);

        Assert.Equal(0, result.Exit);
        Assert.Equal("queued", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{rollback}'"));
        Assert.Equal(0, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{rollback}'"));
        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{first}'"));
        Assert.True((bool)(await ScalarAsync(
            scenario,
            ids[0],
            $"SELECT (SELECT started_at FROM axis.publish_requests WHERE id = '{first}') <= (SELECT started_at FROM axis.publish_requests WHERE id = '{second}')"))!);
        Assert.Equal(2L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
    }

    [Fact]
    public async Task Two_publishers_run_one_request_once_while_the_lease_is_renewed()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null));
        await using var hold = await ReleasesHold.StartAsync(scenario, ids[0]);
        var release = hold.ReleaseAfterAsync(TimeSpan.FromSeconds(7));
        var first = StartPublisher(scenario, _testLease);
        var second = StartPublisher(scenario, _testLease);
        try
        {
            await WaitForStatusAsync(scenario, ids[0], request, "succeeded", "failed");
        }
        finally
        {
            await release;
            await first.StopAsync();
            await second.StopAsync();
        }

        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
    }

    [Fact]
    public async Task A_request_whose_publisher_stopped_is_claimed_again_after_its_lease()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null));
        await SetRunningAsync(scenario, ids[0], request, 1, "now() + interval '3 seconds'");
        var waited = Stopwatch.StartNew();
        var publisher = StartPublisher(scenario, _testLease);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(3));
            Assert.Equal("running", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
            Assert.Equal(1, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));

            await WaitForStatusAsync(scenario, ids[0], request, "succeeded", "failed");
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(2, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
    }

    [Fact]
    public async Task A_request_claimed_three_times_with_an_expired_lease_fails_with_AXC0215()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null));
        await SetRunningAsync(scenario, ids[0], request, 3, "now() - interval '1 minute'");
        var publisher = StartPublisher(scenario, _testLease);
        try
        {
            await WaitForStatusAsync(scenario, ids[0], request, "succeeded", "failed");
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.Equal("failed", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(3, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, ids[0], $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        var diagnostic = Assert.Single(diagnostics)!;
        Assert.Equal("AXC0215", diagnostic["code"]!.GetValue<string>());
        Assert.Equal("error", diagnostic["severity"]!.GetValue<string>());
        Assert.Equal("", diagnostic["file"]!.GetValue<string>());
        Assert.Equal("", diagnostic["path"]!.GetValue<string>());
        Assert.Equal(1L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.publishFailed'"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.releases"));
        Assert.Null(await ScalarAsync(scenario, ids[0], $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
    }

    [Fact]
    public async Task A_publisher_that_lost_its_claim_does_not_finish_the_request()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null));
        await using var hold = await ReleasesHold.StartAsync(scenario, ids[0]);
        var text = new StringWriter();
        var output = TextWriter.Synchronized(text);
        var publisher = StartPublisher(scenario, _testLease, output);
        try
        {
            await WaitForStatusAsync(scenario, ids[0], request, "running");

            // Another publisher claimed the request again. The lease is long, so nothing claims it a third time.
            await ScalarAsync(
                scenario,
                ids[0],
                $"UPDATE axis.publish_requests SET claims = 2, lease_until = now() + interval '1 hour' WHERE id = '{request}'");
            await hold.ReleaseAfterAsync(TimeSpan.Zero);
            await WaitForAsync(() => PublisherOutput(output, text).Contains("claim failed", StringComparison.Ordinal));
        }
        finally
        {
            await hold.ReleaseAfterAsync(TimeSpan.Zero);
            await publisher.StopAsync();
        }

        Assert.Contains($"{ids[0]} claim failed: Another publisher claimed the request again.", PublisherOutput(output, text), StringComparison.Ordinal);
        Assert.Equal("running", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(2, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Null(await ScalarAsync(scenario, ids[0], $"SELECT finished_at FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action LIKE 'release.%'"));
    }

    [Fact]
    public async Task Two_publishers_run_one_request_of_an_application_at_a_time()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var snapshot = await SnapshotAsync(null);
        var older = await QueueAsync(scenario, ids[0], snapshot);
        var newer = await QueueAsync(scenario, ids[0], snapshot);
        await using var hold = await ReleasesHold.StartAsync(scenario, ids[0]);
        var first = StartPublisher(scenario, _testLease);
        var second = StartPublisher(scenario, _testLease);
        try
        {
            await WaitForStatusAsync(scenario, ids[0], older, "running");
            for (var i = 0; i < 10; i++)
            {
                Assert.Equal("running", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{older}'"));
                Assert.Equal("queued", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{newer}'"));
                await Task.Delay(300, CancellationToken);
            }

            await hold.ReleaseAfterAsync(TimeSpan.Zero);
            await WaitForStatusAsync(scenario, ids[0], newer, "succeeded", "failed");
        }
        finally
        {
            await hold.ReleaseAfterAsync(TimeSpan.Zero);
            await first.StopAsync();
            await second.StopAsync();
        }

        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{older}'"));
        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{newer}'"));
        Assert.Equal(1, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{older}'"));
        Assert.Equal(1, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{newer}'"));
        Assert.Equal(2L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
    }

    [Fact]
    public async Task A_validate_with_a_missing_binding_fails_with_the_binding_diagnostic_and_stores_no_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            await platform.TenantBindings.Where(binding => binding.TenantId == ids[0] && binding.Name == UrlBinding).ExecuteDeleteAsync(CancellationToken);
        }

        var releases = await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.releases");
        var active = await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.active_releases");
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null), kind: "validate");

        var result = await RunPublisherAsync(scenario, ids[0], request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var diagnostics = JsonNode.Parse((string)(await ScalarAsync(scenario, ids[0], $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        Assert.Contains(diagnostics, item => item!["code"]!.GetValue<string>() == "AXC0100" && item["severity"]!.GetValue<string>() == "error");
        Assert.Equal($"{ids[0]} claim ok", result.Output[0]);
        Assert.Equal($"{ids[0]} version ok", result.Output[1]);
        Assert.StartsWith($"{ids[0]} validate failed: AXC0100", result.Output[2], StringComparison.Ordinal);
        Assert.DoesNotContain(result.Output, line => line.Contains(" compile ", StringComparison.Ordinal) || line.Contains(" activate ", StringComparison.Ordinal) || line.Contains(" seed ", StringComparison.Ordinal));
        Assert.Equal(releases, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.releases"));
        Assert.Equal(active, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_valid_validate_succeeds_and_leaves_no_release_active_release_entity_table_or_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var request = await QueueAsync(scenario, ids[0], await SnapshotAsync(null), kind: "validate");

        var result = await RunPublisherAsync(scenario, ids[0], request);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal([$"{ids[0]} claim ok", $"{ids[0]} version ok", $"{ids[0]} validate ok"], result.Output);
        Assert.Equal("succeeded", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Null(await ScalarAsync(scenario, ids[0], $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(1, await ScalarAsync(scenario, ids[0], $"SELECT claims FROM axis.publish_requests WHERE id = '{request}'"));
        foreach (var table in new[] { "releases", "active_releases", "active_sites", "provisioned_entities", "applied_migrations", "audit_records" })
        {
            Assert.Equal(0L, await ScalarAsync(scenario, ids[0], $"SELECT count(*) FROM axis.{table}"));
        }

        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'entities'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_validate_does_not_change_the_active_release_and_a_validate_of_an_unreadable_snapshot_fails_at_validate()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", ids[0]])).Exit);
        var before = await ScalarAsync(scenario, ids[0], "SELECT release_id FROM axis.active_releases");
        var releases = await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.releases");
        var unreadable = await QueueAsync(scenario, ids[0], "{}", kind: "validate");

        var result = await RunPublisherAsync(scenario, ids[0], unreadable);

        Assert.Equal("failed", await ScalarAsync(scenario, ids[0], $"SELECT status FROM axis.publish_requests WHERE id = '{unreadable}'"));
        Assert.Equal([$"{ids[0]} claim ok", $"{ids[0]} version ok", $"{ids[0]} validate failed: The snapshot cannot be read."], result.Output);
        Assert.Equal(before, await ScalarAsync(scenario, ids[0], "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(releases, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(scenario, ids[0], "SELECT count(*) FROM axis.audit_records"));
    }

    [Fact]
    public async Task Publisher_exits_0_after_a_stop_and_2_for_an_extra_argument_an_invalid_source_or_a_missing_tenant_configuration()
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
        Assert.All([unset, configuration], result => Assert.Contains("Tenants", result.Error[0], StringComparison.Ordinal));
        Assert.Contains("Tenancy:Source", invalid.Error[0], StringComparison.Ordinal);
        Assert.DoesNotContain("nowhere", invalid.Error[0], StringComparison.Ordinal);
        Assert.Contains("Tenancy:RefreshInterval", refresh.Error[0], StringComparison.Ordinal);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Under_the_configuration_source_a_queued_publish_runs_and_its_release_becomes_active()
    {
        var schema = await MigratedTenantDatabaseAsync();
        var request = await QueueConfiguredAsync(schema, await SnapshotAsync(null));

        var result = await RunConfiguredPublisherAsync(schema, request);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal("default ready", result.Output[0]);
        Assert.Equal("default claim ok", result.Output[1]);
        Assert.Equal(_steps.Select(step => $"default {step} ok"), result.Output.Skip(2));
        Assert.Equal("succeeded", await ConfiguredScalarAsync(schema, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"));
        var release = await ConfiguredScalarAsync(schema, $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'");
        Assert.NotNull(release);
        Assert.Equal(release, await ConfiguredScalarAsync(schema, "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(1L, await ConfiguredScalarAsync(schema, "SELECT count(*) FROM axis.audit_records WHERE action = 'release.published'"));
        Assert.All(
            result.Output.Concat(result.Error),
            line =>
            {
                Assert.DoesNotContain("Password", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(schema, line, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Under_the_configuration_source_a_publisher_started_before_the_migration_waits_and_then_runs_the_request()
    {
        var schema = await database.CreateDatabaseAsync("publisher", CancellationToken);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var output = new SynchronizedWriter();
        var run = RunConfiguredAsync(ConfiguredSettings(schema), output, stop.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken);
            Assert.False(run.IsCompleted);
            Assert.Contains("default waiting", output.Lines().Single(), StringComparison.Ordinal);

            await MigrateAsync(schema);
            var request = await QueueConfiguredAsync(schema, await SnapshotAsync(null));
            await WaitForStatusAsync(schema, request, run);
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await run;
        var lines = output.Lines();
        Assert.Equal(0, result.Exit);
        Assert.Equal(["default waiting: the database is not migrated yet.", "default ready", "default claim ok"], lines.Take(3));
        Assert.Equal(1, lines.Count(line => line.StartsWith("default waiting", StringComparison.Ordinal)));
        Assert.Equal(1, lines.Count(line => line == "default ready"));
    }

    [Fact]
    public async Task Under_the_configuration_source_a_tenant_without_a_schema_connection_string_exits_2_and_names_the_setting()
    {
        var schema = await MigratedTenantDatabaseAsync();
        var settings = ConfiguredSettings(schema);
        settings.Remove("Tenants:default:SchemaConnectionString");
        var output = new SynchronizedWriter();

        var result = await RunConfiguredAsync(settings, output, CancellationToken);

        Assert.Equal(2, result.Exit);
        Assert.Empty(output.Lines());
        var message = Assert.Single(result.Error);
        Assert.Contains("Tenants:default:SchemaConnectionString", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PostgreSqlServer.RolePassword + ";", message, StringComparison.Ordinal);
    }

    // A publisher whose lease is short, so that a test sees renewal and reclaim in seconds.
    private static readonly TimeSpan _testLease = TimeSpan.FromSeconds(2);

    private sealed record RunningPublisher(CancellationTokenSource Stop, Task<AdminResult> Run)
    {
        public async Task<AdminResult> StopAsync()
        {
            await Stop.CancelAsync();
            var result = await Run;
            Stop.Dispose();
            return result;
        }
    }

    // The synchronized writer takes its own lock for each write, so a read under that lock sees whole lines.
    private static string PublisherOutput(TextWriter synchronized, StringWriter text)
    {
        lock (synchronized)
        {
            return text.ToString();
        }
    }

    private static RunningPublisher StartPublisher(AdminScenario scenario, TimeSpan lease, TextWriter? output = null)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        return new RunningPublisher(stop, scenario.RunAsync(["publisher"], output, stop, extra: _directory, publisherLease: lease));
    }

    // Holds a lock on the table of releases, so that a request stops at its activation for a time the test chooses.
    private sealed class ReleasesHold : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private bool _released;

        private ReleasesHold(NpgsqlConnection connection) => _connection = connection;

        public static async Task<ReleasesHold> StartAsync(AdminScenario scenario, string tenant)
        {
            var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(await PostgreSqlServer.AdminConnectionStringAsync())
            {
                Database = $"axis_{tenant}",
                Pooling = false,
            }.ConnectionString);
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand("BEGIN; LOCK TABLE axis.releases IN ACCESS EXCLUSIVE MODE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken);
            return new ReleasesHold(connection);
        }

        // Ends the transaction after the delay. Calling it again does nothing.
        public async Task ReleaseAfterAsync(TimeSpan delay)
        {
            await Task.Delay(delay, CancellationToken);
            if (_released)
            {
                return;
            }

            _released = true;
            await using var command = new NpgsqlCommand("COMMIT", _connection);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    // Makes the row look as if a publisher claimed it and then died: running, with the given claims and lease.
    private static async Task SetRunningAsync(AdminScenario scenario, string tenant, Guid request, int claims, string leaseSql) =>
        await ScalarAsync(
            scenario,
            tenant,
            $"UPDATE axis.publish_requests SET status = 'running', claimed_by = 'dead-publisher', claims = {claims}, started_at = now(), lease_until = {leaseSql} WHERE id = '{request}'");

    private static async Task WaitForStatusAsync(AdminScenario scenario, string tenant, Guid request, params string[] statuses) =>
        await WaitForAsync(async () =>
        {
            var status = (string?)await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
            return Array.IndexOf(statuses, status) >= 0;
        });

    private static async Task WaitForAsync(Func<bool> condition) => await WaitForAsync(() => Task.FromResult(condition()));

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 60 seconds.");
            await Task.Delay(200, CancellationToken);
        }
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
        return await InsertRequestAsync(connection, snapshot, kind);
    }

    private static async Task<Guid> InsertRequestAsync(NpgsqlConnection connection, string snapshot, string kind)
    {
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

    // A tenant database that the server would have migrated and granted.
    private async Task<string> MigratedTenantDatabaseAsync()
    {
        var schema = await database.CreateDatabaseAsync("publisher", CancellationToken);
        await MigrateAsync(schema);
        return schema;
    }

    private static async Task MigrateAsync(string schema)
    {
        await using (var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(schema).Options))
        {
            await configuration.Database.MigrateAsync(CancellationToken);
        }

        await using (var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(schema).Options))
        {
            await data.Database.MigrateAsync(CancellationToken);
        }

        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(schema).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(schema, CancellationToken);
    }

    // The settings of one tenant, 'default', under the configuration source.
    private static Dictionary<string, string?> ConfiguredSettings(string schema) => new()
    {
        ["Tenancy:Source"] = "configuration",
        ["Tenants:default:Hosts:0"] = "default.example.test",
        ["Tenants:default:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(schema),
        ["Tenants:default:SchemaConnectionString"] = schema,
        [$"Tenants:default:Bindings:{UrlBinding}:Kind"] = "url",
        [$"Tenants:default:Bindings:{UrlBinding}:Value"] = "https://purchasing.example.test",
        [$"Tenants:default:Bindings:{SecretBinding}:Kind"] = "secret",
        [$"Tenants:default:Bindings:{SecretBinding}:Value"] = "binding-secret-value",
        ["Extensions:Path"] = SamplePackages.Path,
        ["Modules:Path"] = SampleModules.Path,
    };

    private static async Task<AdminResult> RunConfiguredAsync(Dictionary<string, string?> settings, SynchronizedWriter output, CancellationToken stop)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var error = new StringWriter();
        var exit = await AdminCommands.RunAsync(["publisher"], configuration, output, error, stop);
        return new AdminResult(exit, output.Lines(), error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    // Runs the publisher until the request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunConfiguredPublisherAsync(string schema, Guid request)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var output = new SynchronizedWriter();
        var run = RunConfiguredAsync(ConfiguredSettings(schema), output, stop.Token);
        try
        {
            await WaitForStatusAsync(schema, request, run);
        }
        finally
        {
            await stop.CancelAsync();
        }

        return await run;
    }

    private static async Task WaitForStatusAsync(string schema, Guid request, Task<AdminResult> run)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!run.IsCompleted && DateTime.UtcNow < deadline)
        {
            var status = await ConfiguredScalarAsync(schema, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
            if (status is "succeeded" or "failed")
            {
                Assert.Equal("succeeded", status);
                return;
            }

            await Task.Delay(200, CancellationToken);
        }

        Assert.Fail("The request did not succeed in time.");
    }

    private static Task<object?> ConfiguredScalarAsync(string schema, string sql) =>
        PostgreSqlServer.AdminScalarAsync(schema, sql, CancellationToken);

    // Stores a request over the runtime role, as the server will.
    private static async Task<Guid> QueueConfiguredAsync(string schema, string snapshot)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(schema));
        return await InsertRequestAsync(connection, snapshot, "publish");
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

    // The test reads the output while the publisher writes it.
    private sealed class SynchronizedWriter : StringWriter
    {
        private readonly object _lock = new();

        public override void WriteLine(string? value)
        {
            lock (_lock)
            {
                base.WriteLine(value);
            }
        }

        public override string ToString()
        {
            lock (_lock)
            {
                return base.ToString();
            }
        }

        public string[] Lines() => ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
