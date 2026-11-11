using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.ControlPlane.Billing;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The PayOS webhook against a real PostgreSQL, see <c>docs/reference/payments.md#webhooks</c>: the checksum is checked first, a signed event is kept once in the
/// inbox, and the <c>applyPaymentWebhooks</c> job applies the events in the order of PayOS's transfer time as <c>payment</c> ledger entries. An event that does not fit its
/// invoice is kept, applies nothing and alerts. The tests sign their bodies with their own copy of PayOS's rule, so the helper under test is not its own witness.
/// </summary>
public sealed class PaymentWebhookTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string ChecksumKey = "test-checksum-key";
    private const long PlanVnd = 4_900_000;

    // Ten digits, so no other number of a body or a log entry can hold it by chance.
    private const string Marker = "4829173650";

    private static readonly DateTimeOffset _now = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _periodStart = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = BillingSeed.Catalogue();
    private readonly LogCollector _logs = new();
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), token);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_wrong_checksum_is_refused_with_401_and_stores_nothing_and_a_signed_event_is_kept()
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var invoice = await InvoiceAsync("checksum");
        var orderCode = await OrderCodeAsync(invoice);

        var wrong = await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 1_000_000, "FT1001", "2026-11-10 15:00:00", key: "another-key")), token);
        var unsigned = await client.PostAsync("/api/payments/payos/webhook", Json("""{"code":"00","data":{"orderCode":1,"amount":1,"reference":"FT1002"}}"""), token);
        var notJson = await client.PostAsync("/api/payments/payos/webhook", Json("not json " + Marker), token);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, notJson.StatusCode);
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.webhook_inbox"));
        Assert.Equal(3, _logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("PayOS webhook was refused", StringComparison.Ordinal)));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(Marker, StringComparison.Ordinal));

        var signed = await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 1_000_000, "FT1003", "2026-11-10 15:00:00")), token);

        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM billing.webhook_inbox WHERE provider = 'payos' AND event_id = 'FT1003' AND outcome IS NULL"));
    }

    [Fact]
    public async Task A_signed_event_without_a_reference_an_order_code_or_an_amount_is_refused_with_400_and_stores_nothing()
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        foreach (var missing in new[] { "reference", "orderCode", "amount" })
        {
            var data = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["orderCode"] = 7L, ["amount"] = 1000L, ["reference"] = "FT2001" };
            data.Remove(missing);

            var response = await client.PostAsync("/api/payments/payos/webhook", Json(Sign(data)), token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.webhook_inbox"));
    }

    [Fact]
    public async Task The_same_event_posted_three_times_leaves_one_inbox_row_and_after_the_job_one_payment()
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var invoice = await InvoiceAsync("repeat");
        var body = Event(await OrderCodeAsync(invoice), 1_000_000, "FT3001", "2026-11-10 15:00:00");

        for (var delivery = 0; delivery < 3; delivery++)
        {
            var response = await client.PostAsync("/api/payments/payos/webhook", Json(body), token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM billing.webhook_inbox"));

        await RunJobAsync(token);
        await RunJobAsync(token);

        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries WHERE kind = 'payment'"));
        Assert.Equal(
            $"-1000000|FT3001|payos:FT3001|{invoice}",
            await ScalarAsync<string>("SELECT concat_ws('|', amount_minor, provider_reference, idempotency_key, invoice_id) FROM billing.ledger_entries WHERE kind = 'payment'"));
        Assert.Equal("applied", await ScalarAsync<string>("SELECT outcome FROM billing.webhook_inbox"));
        Assert.Equal(
            1,
            await ScalarAsync<long>($"SELECT count(*) FROM control_plane.audit_records WHERE action = 'payment.recorded' AND actor_kind = 'webhook' AND subject = '{invoice}'"));
    }

    [Fact]
    public async Task Two_payments_that_reach_the_amount_mark_the_invoice_paid_and_the_first_alone_does_not()
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var invoice = await InvoiceAsync("pay");
        var orderCode = await OrderCodeAsync(invoice);

        await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 2_000_000, "FT4001", "2026-11-10 15:00:00")), token);
        await RunJobAsync(token);

        Assert.Equal("open", await ScalarAsync<string>($"SELECT status FROM billing.invoices WHERE id = '{invoice}'"));

        await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, PlanVnd - 2_000_000, "FT4002", "2026-11-10 16:00:00")), token);
        await RunJobAsync(token);

        Assert.Equal("paid", await ScalarAsync<string>($"SELECT status FROM billing.invoices WHERE id = '{invoice}'"));
        Assert.Equal(2, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries WHERE kind = 'payment'"));
    }

    [Fact]
    public async Task Events_posted_in_the_reverse_of_their_transfer_time_are_applied_in_the_order_of_that_time()
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var invoice = await InvoiceAsync("order");
        var orderCode = await OrderCodeAsync(invoice);

        await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 1_000_000, "FT5003", "2026-11-10 17:00:00")), token);
        await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 1_000_000, "FT5001", "2026-11-10 15:00:00")), token);
        await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 1_000_000, "FT5002", "2026-11-10 16:00:00")), token);
        await RunJobAsync(token);

        Assert.Equal(
            "FT5001,FT5002,FT5003",
            await ScalarAsync<string>("SELECT string_agg(provider_reference, ',' ORDER BY occurred_at, id) FROM billing.ledger_entries WHERE kind = 'payment'"));
    }

    [Fact]
    public async Task An_event_for_a_void_invoice_is_kept_as_out_of_place_writes_no_entry_logs_an_error_and_adds_to_the_metric()
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var invoice = await InvoiceAsync("void");
        var orderCode = await OrderCodeAsync(invoice);
        await Execute($"UPDATE billing.invoices SET status = 'void' WHERE id = '{invoice}'");
        await client.PostAsync("/api/payments/payos/webhook", Json(Event(orderCode, 1_000_000, "FT6001", "2026-11-10 15:00:00")), token);
        var measurements = new ConcurrentBag<(long Value, string? Provider, string? Reason)>();
        using var listener = Listen(measurements);

        await RunJobAsync(token);

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries WHERE kind = 'payment'"));
        Assert.Equal("outOfPlace|voidInvoice", await ScalarAsync<string>("SELECT concat_ws('|', outcome, reason) FROM billing.webhook_inbox WHERE event_id = 'FT6001'"));
        Assert.Equal("void", await ScalarAsync<string>($"SELECT status FROM billing.invoices WHERE id = '{invoice}'"));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'payment.recorded'"));
        var error = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("voidInvoice", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(Marker, StringComparison.Ordinal));
        var measurement = Assert.Single(measurements);
        Assert.Equal((1L, "payos", "voidInvoice"), measurement);

        await RunJobAsync(token);

        Assert.Single(measurements);
    }

    [Theory]
    [InlineData("unknownOrder")]
    [InlineData("notPaid")]
    [InlineData("currency")]
    public async Task An_event_that_does_not_fit_its_invoice_is_kept_as_out_of_place_and_writes_no_entry(string reason)
    {
        var token = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var invoice = await InvoiceAsync("misfit");
        var orderCode = await OrderCodeAsync(invoice);
        var body = reason switch
        {
            "unknownOrder" => Event(orderCode + 1000, 1_000_000, "FT7001", "2026-11-10 15:00:00"),
            "notPaid" => Event(orderCode, 1_000_000, "FT7001", "2026-11-10 15:00:00", code: "01"),
            _ => Event(orderCode, 1_000_000, "FT7001", "2026-11-10 15:00:00", currency: "USD"),
        };
        await client.PostAsync("/api/payments/payos/webhook", Json(body), token);

        await RunJobAsync(token);

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries WHERE kind = 'payment'"));
        Assert.Equal($"outOfPlace|{reason}", await ScalarAsync<string>("SELECT concat_ws('|', outcome, reason) FROM billing.webhook_inbox"));
        Assert.Equal("open", await ScalarAsync<string>($"SELECT status FROM billing.invoices WHERE id = '{invoice}'"));
        Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // The signed body of a PayOS payment event. The account number holds the marker, which no log may show.
    private static string Event(long orderCode, long amount, string reference, string time, string code = "00", string currency = "VND", string key = ChecksumKey) =>
        Sign(
            new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["orderCode"] = orderCode,
                ["amount"] = amount,
                ["description"] = $"AXIS {orderCode}",
                ["accountNumber"] = Marker,
                ["reference"] = reference,
                ["transactionDateTime"] = time,
                ["currency"] = currency,
                ["code"] = code,
                ["desc"] = "success",
            },
            key);

    // PayOS's rule written out again: the keys in ordinal order as key=value joined by &, signed with HMAC-SHA256 and written in lowercase hex.
    private static string Sign(SortedDictionary<string, object> data, string key = ChecksumKey)
    {
        var text = string.Join('&', data.Select(pair => $"{pair.Key}={Convert.ToString(pair.Value, CultureInfo.InvariantCulture)}"));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(text)));
        return JsonSerializer.Serialize(new { code = "00", desc = "success", success = true, data, signature });
    }

    private static MeterListener Listen(ConcurrentBag<(long Value, string? Provider, string? Reason)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == ControlPlaneMetrics.PaymentsOutOfPlace)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? provider = null;
            string? reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == ControlPlaneMetrics.PaymentProviderTag)
                {
                    provider = tag.Value as string;
                }
                else if (tag.Key == ControlPlaneMetrics.PaymentReasonTag)
                {
                    reason = tag.Value as string;
                }
            }

            measurements.Add((value, provider, reason));
        });
        listener.Start();
        return listener;
    }

    private string ControlPlaneConnectionString => $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5";

    // The recurring jobs are removed, so only the test's own runs of the job apply the events. The migration at the start of the host finds the schema as it is.
    private async Task<WebApplicationFactory<Program>> FactoryAsync()
    {
        var host = await mailpit.SmtpHostAsync();
        var port = await mailpit.SmtpPortAsync();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", ControlPlaneConnectionString);
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
            builder.UseSetting("ControlPlane:PayOs:Provider", "Fake");
            builder.UseSetting("ControlPlane:PayOs:ChecksumKey", ChecksumKey);
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.ConfigureServices(services => services.RemoveAll<RecurringJob>());
        });
    }

    // Runs the job once, at the fixed clock.
    private Task RunJobAsync(CancellationToken token)
    {
        using var loggers = new LoggerFactory([_logs]);
        return new PaymentWebhookEvents(_dataSource, new FixedClock(_now), loggers.CreateLogger<PaymentWebhookEvents>())
            .RunAsync(new ClaimedJob(Guid.CreateVersion7(), PaymentWebhookEvents.JobKind, [], 1), token);
    }

    // The period invoice of a new subscription, in its own transaction.
    private async Task<Guid> InvoiceAsync(string tenant)
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await BillingSeed.SubscriptionAsync(_dataSource, tenant, "month", token);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var invoice = await Invoices.CreatePeriodAsync(transaction, subscription, _periodStart, _catalogue, _now, token);
        await transaction.CommitAsync(token);
        return invoice.Id;
    }

    // The orderCode that PayOS would send back: the invoice's link row gets it from the sequence.
    private async Task<long> OrderCodeAsync(Guid invoice)
    {
        await Execute($"INSERT INTO billing.payment_links (invoice_id) VALUES ('{invoice}')");
        return await ScalarAsync<long>($"SELECT order_code FROM billing.payment_links WHERE invoice_id = '{invoice}'");
    }

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
