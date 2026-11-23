using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.ControlPlane.BillingEvents;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The <c>usage.daily</c> event of each tenant's finished day, on a real PostgreSQL against a cell that <see cref="CellStub"/> stands in for and a
/// receiver that a message handler stands in for, see <c>docs/reference/billing-api.md#daily-usage</c>. The clock is fixed at 03:00 UTC of today, so
/// yesterday's row is final, and the job runner has no backoff. Each test class instance has a container of its own, with the region <c>vn</c>, the
/// cell <c>vn-1</c> that the control plane reaches and the cell <c>vn-9</c> that it does not.
/// </summary>
public sealed class DailyUsageEventTests : IAsyncLifetime
{
    private const string Url = "https://billing.example/events";
    private const string Secret = "daily-usage-secret";
    private const string TenantA = "usage-4172903856";
    private const string TenantB = "usage-5938162047";
    private const string DeletedTenant = "usage-7305182946";

    private static readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly _yesterday = _today.AddDays(-1);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly CellStub _cell = new();
    private readonly ReceiverStub _receiver = new();
    private readonly MovedClock _clock = new(new DateTimeOffset(_today.ToDateTime(new TimeOnly(3, 0)), TimeSpan.Zero));
    private readonly ILoggerFactory _loggers = LoggerFactory.Create(_ => { });
    private NpgsqlDataSource _dataSource = null!;
    private CellClients _clients = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString)
            .Options;
        await using (var context = new DirectoryDbContext(options))
        {
            await context.Database.MigrateAsync(token);
        }

        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), token);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await Execute("INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('vn', 'vn.example.com', 'https://api.vn.example.com')");
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('vn-1', 'vn', 'https://vn-1.test', 100, 3)");
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('vn-9', 'vn', 'https://vn-9.test', 100, 0)");

        var controlPlane = new ControlPlaneOptions { TenantDomain = "vn.example.com", PublicUrl = "https://account.vn.example.com", MailFrom = "no-reply@vn.example.com" };
        controlPlane.Cells.Add(new CellOptions { Id = "vn-1", InternalUrl = "https://vn-1.test", TokenUrl = "https://vn-1.test/token", ClientId = "axis-controlplane" });
        controlPlane.CellSecrets["vn-1"] = "cell-secret";
        _clients = new CellClients(controlPlane, new StubHttpClientFactory(_cell), new CellClientOptions(), _clock);
        _cell.UsageDay = Row(_yesterday);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.Dispose();
        _loggers.Dispose();
        _cell.Dispose();
        _receiver.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_run_delivers_one_signed_usage_daily_for_yesterday_with_the_seven_values_of_the_cell_row()
    {
        await SeedAsync(TenantA, "vn-1", "active");

        await RunAsync(billingEvents: true);
        await DeliverAsync(billingEvents: true);

        var read = Assert.Single(_cell.UsageDayReads);
        var filter = Uri.EscapeDataString($$"""{"all":[{"field":"day","op":"eq","value":"{{Text(_yesterday)}}"}]}""");
        Assert.Equal($"/api/cell/tenants/{TenantA}/usage-days?filter={filter}&pageSize=1", read);
        var request = Assert.Single(_receiver.Requests);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        var id = body["id"]!.GetValue<string>();
        Assert.Equal("usage.daily", body["type"]!.GetValue<string>());
        Assert.Equal("v1", body["apiVersion"]!.GetValue<string>());
        Assert.Equal(Text(_today) + "T00:00:00.000Z", body["timestamp"]!.GetValue<string>());
        var data = body["data"]!.AsObject();
        Assert.Equal(TenantA, data["tenantId"]!.GetValue<string>());
        Assert.Equal(Text(_yesterday), data["day"]!.GetValue<string>());
        Assert.Equal(1_100L, data["databaseBytes"]!.GetValue<long>());
        Assert.Equal(2_200L, data["fileBytes"]!.GetValue<long>());
        Assert.Equal(3, data["users"]!.GetValue<int>());
        Assert.Equal(4, data["applications"]!.GetValue<int>());
        Assert.Equal(5, data["emailsSent"]!.GetValue<int>());
        Assert.Equal(6_600L, data["connectorCalls"]!.GetValue<long>());
        Assert.Equal(7_700L, data["workItemsRun"]!.GetValue<long>());
        Assert.Equal(9, data.Count);
        Assert.Equal(id, request.Headers["webhook-id"]);

        // The signature is recomputed here from the Standard Webhooks text, and not with the code under test.
        var unixSeconds = request.Headers["webhook-timestamp"];
        var signed = Encoding.UTF8.GetBytes($"{id}.{unixSeconds}.{request.Body}");
        Assert.Equal("v1," + Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), signed)), request.Headers["webhook-signature"]);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));
    }

    [Fact]
    public async Task A_row_without_users_and_applications_is_sent_with_null_for_both()
    {
        await SeedAsync(TenantA, "vn-1", "active");
        _cell.UsageDay = new
        {
            day = Text(_yesterday),
            databaseBytes = 1L,
            fileBytes = 2L,
            users = (int?)null,
            applications = (int?)null,
            emailsSent = 0,
            connectorCalls = 0L,
            workItemsRun = 0L,
        };

        await RunAsync(billingEvents: true);
        await DeliverAsync(billingEvents: true);

        var data = JsonNode.Parse(Assert.Single(_receiver.Requests).Body)!["data"]!.AsObject();
        Assert.True(data.ContainsKey("users"));
        Assert.Null(data["users"]);
        Assert.True(data.ContainsKey("applications"));
        Assert.Null(data["applications"]);
        Assert.Equal(0, data["emailsSent"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_second_run_queues_no_job_and_a_deleted_subscription_gets_no_cell_read_and_no_event()
    {
        await SeedAsync(TenantA, "vn-1", "active");
        await SeedAsync(DeletedTenant, "vn-1", "deleted");

        await RunAsync(billingEvents: true);
        await RunAsync(billingEvents: true);

        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));
        var read = Assert.Single(_cell.UsageDayReads);
        Assert.Contains(TenantA, read, StringComparison.Ordinal);
        Assert.DoesNotContain(DeletedTenant, read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Before_two_in_the_morning_a_run_sends_nothing_and_calls_no_cell()
    {
        await SeedAsync(TenantA, "vn-1", "active");
        _clock.Now = new DateTimeOffset(_today.ToDateTime(new TimeOnly(1, 30)), TimeSpan.Zero);

        await RunAsync(billingEvents: true);

        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));
        Assert.Empty(_cell.UsageDayReads);
    }

    [Fact]
    public async Task Without_the_billing_events_address_a_run_queues_no_job_writes_no_record_and_calls_no_cell()
    {
        await SeedAsync(TenantA, "vn-1", "active");

        await RunAsync(billingEvents: false);

        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));
        Assert.Empty(_cell.UsageDayReads);
        Assert.Empty(_cell.Calls);
    }

    [Fact]
    public async Task A_tenant_whose_cell_is_not_one_of_the_regions_is_skipped_and_the_run_throws_and_a_later_run_sends_only_its_event()
    {
        await SeedAsync(TenantA, "vn-1", "active");
        await SeedAsync(TenantB, "vn-9", "active");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(billingEvents: true));

        Assert.Equal("The daily usage of 1 subscriptions could not be read from their cells.", exception.Message);
        Assert.DoesNotContain(TenantB, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));

        await Execute($"UPDATE directory.tenants SET cell_id = 'vn-1' WHERE id = '{TenantB}'");
        await RunAsync(billingEvents: true);
        await DeliverAsync(billingEvents: true);

        var tenants = _receiver.Requests.Select(request => JsonNode.Parse(request.Body)!["data"]!["tenantId"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal([TenantA, TenantB], tenants);
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));
    }

    [Fact]
    public async Task A_failed_cell_read_skips_the_tenant_and_a_cell_without_a_row_for_the_day_sends_no_event_and_does_not_fail_the_run()
    {
        await SeedAsync(TenantA, "vn-1", "active");
        _cell.FailReads = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(billingEvents: true));

        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));

        _cell.FailReads = false;
        _cell.UsageDay = null;
        await RunAsync(billingEvents: true);

        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM control_plane.usage_day_events"));
    }

    [Fact]
    public async Task A_run_deletes_the_records_older_than_thirty_days_and_keeps_the_newer_ones()
    {
        var id = await SeedAsync(TenantA, "vn-1", "active");
        await Execute($"INSERT INTO control_plane.usage_day_events (subscription_id, day) VALUES ('{id}', '{Text(_yesterday.AddDays(-31))}'), ('{id}', '{Text(_yesterday.AddDays(-30))}')");

        await RunAsync(billingEvents: true);

        Assert.Equal(
            [Text(_yesterday.AddDays(-30)), Text(_yesterday)],
            await Strings("SELECT to_char(day, 'YYYY-MM-DD') FROM control_plane.usage_day_events ORDER BY day"));
    }

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object Row(DateOnly day) => new
    {
        day = Text(day),
        databaseBytes = 1_100L,
        fileBytes = 2_200L,
        users = 3,
        applications = 4,
        emailsSent = 5,
        connectorCalls = 6_600L,
        workItemsRun = 7_700L,
    };

    private static ControlPlaneOptions Options(bool billingEvents) => new()
    {
        TenantDomain = "vn.example.com",
        PublicUrl = "https://account.vn.example.com",
        MailFrom = "no-reply@vn.example.com",
        BillingEvents = billingEvents ? new ControlPlaneBillingEventsOptions { Url = Url, Secret = Secret } : new ControlPlaneBillingEventsOptions(),
    };

    private Task RunAsync(bool billingEvents) =>
        new DailyUsageEvents(_dataSource, _dataSource, _clients, Options(billingEvents), _clock, _loggers.CreateLogger<DailyUsageEvents>())
            .RunAsync(new ClaimedJob(Guid.NewGuid(), DailyUsageEvents.JobKind, [], 1), TestContext.Current.CancellationToken);

    // Runs the queued sendBillingEvent jobs against the receiver, with no backoff.
    private async Task DeliverAsync(bool billingEvents)
    {
        var handler = new SendBillingEventJobHandler(_dataSource, Options(billingEvents), new StubHttpClientFactory(_receiver), _clock);
        var runner = new JobRunner(_dataSource, [handler], new JobRunnerOptions { BaseDelay = TimeSpan.Zero }, _loggers.CreateLogger<JobRunner>());
        while (await runner.RunOnceAsync(TestContext.Current.CancellationToken))
        {
        }
    }

    // A sign-up and a subscription created two days ago, so the subscription existed on the whole of yesterday, and the tenant's row in the directory.
    private async Task<Guid> SeedAsync(string tenant, string cell, string state)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        await Execute($"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, state, trial_ends_at, step_id, created_at)
            VALUES ('{id}', '{tenant}', '{signUp}', '{state}', now(), '{Guid.CreateVersion7()}', now() - interval '2 days')
            """);
        await Execute($"INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('{tenant}', 'vn', '{cell}') ON CONFLICT DO NOTHING");
        return id;
    }

    private async Task Execute(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private Task<T> Scalar<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private async Task<List<string>> Strings(string sql)
    {
        var values = new List<string>();
        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed class MovedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record ReceivedRequest(IReadOnlyDictionary<string, string> Headers, string Body);

    // Stands in for the billing system's receiver: it records each request and answers 200.
    private sealed class ReceiverStub : HttpMessageHandler
    {
        public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Enqueue(new ReceivedRequest(headers, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
