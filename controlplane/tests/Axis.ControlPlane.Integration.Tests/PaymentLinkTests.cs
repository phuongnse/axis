using Axis.ControlPlane.Billing;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The <c>createPaymentLinks</c> job against a real PostgreSQL and a fake PayOS, see <c>docs/reference/payments.md#paying-in-vietnam</c>: one link
/// for each open <c>VND</c> invoice, a unique <c>orderCode</c> that is saved before PayOS is called and reused by a retry, and no link for a paid or
/// <c>USD</c> invoice.
/// </summary>
public sealed class PaymentLinkTests : IAsyncLifetime
{
    private const long PlanVnd = 4_900_000;

    private static readonly DateTimeOffset _now = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _periodStart = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = BillingSeed.Catalogue();
    private readonly FakePayOs _payOs = new();
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
    public async Task An_open_VND_invoice_gets_one_link_and_a_second_run_adds_no_row_and_sends_no_request()
    {
        var token = TestContext.Current.CancellationToken;
        var invoice = await InvoiceAsync("link", "month");

        await RunJobAsync(token);
        var orderCode = await ScalarAsync<long>($"SELECT order_code FROM billing.payment_links WHERE invoice_id = '{invoice}'");
        await RunJobAsync(token);

        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM billing.payment_links"));
        Assert.Equal(
            $"fake-{orderCode}|https://payos.invalid/web/fake-{orderCode}|fake-vietqr-{orderCode}|970000|0000000000|AXIS FAKE|AXIS {orderCode}",
            await ScalarAsync<string>(
                $"SELECT concat_ws('|', payment_link_id, checkout_url, qr_code, bin, account_number, account_name, description) FROM billing.payment_links WHERE invoice_id = '{invoice}'"));
        Assert.Equal(_now, await ScalarAsync<DateTime>($"SELECT created_at FROM billing.payment_links WHERE invoice_id = '{invoice}'"));
        var request = Assert.Single(_payOs.Requests);
        Assert.Equal(new PayOsPaymentRequest(orderCode, PlanVnd, $"AXIS {orderCode}"), request);
        Assert.Equal(1, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.audit_records WHERE action = 'paymentLink.created' AND subject = '{invoice}' AND actor_kind = 'job'"));
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'paymentLink.created'"));
    }

    [Fact]
    public async Task After_a_failed_call_the_next_run_stores_the_link_with_the_same_order_code()
    {
        var token = TestContext.Current.CancellationToken;
        var invoice = await InvoiceAsync("retry", "month");
        _payOs.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunJobAsync(token));

        var orderCode = await ScalarAsync<long>($"SELECT order_code FROM billing.payment_links WHERE invoice_id = '{invoice}'");
        Assert.True(await ScalarAsync<bool>($"SELECT created_at IS NULL FROM billing.payment_links WHERE invoice_id = '{invoice}'"));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'paymentLink.created'"));

        await RunJobAsync(token);

        Assert.Equal(orderCode, await ScalarAsync<long>($"SELECT order_code FROM billing.payment_links WHERE invoice_id = '{invoice}'"));
        Assert.True(await ScalarAsync<bool>($"SELECT created_at IS NOT NULL FROM billing.payment_links WHERE invoice_id = '{invoice}'"));
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM billing.payment_links"));
        Assert.Equal([orderCode, orderCode], _payOs.Requests.Select(r => r.OrderCode));
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'paymentLink.created'"));
    }

    [Fact]
    public async Task Two_invoices_get_different_order_codes_and_the_database_refuses_a_repeated_one()
    {
        var token = TestContext.Current.CancellationToken;
        var first = await InvoiceAsync("first", "month");
        var second = await InvoiceAsync("second", "month");

        await RunJobAsync(token);

        var firstCode = await ScalarAsync<long>($"SELECT order_code FROM billing.payment_links WHERE invoice_id = '{first}'");
        var secondCode = await ScalarAsync<long>($"SELECT order_code FROM billing.payment_links WHERE invoice_id = '{second}'");
        Assert.NotEqual(firstCode, secondCode);
        Assert.Equal(2, _payOs.Requests.Select(r => r.OrderCode).Distinct().Count());

        var third = await InvoiceAsync("third", "month");
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute($"INSERT INTO billing.payment_links (invoice_id, order_code) VALUES ('{third}', {firstCode})"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PaymentLinkRow.MaxOrderCode + 1)]
    public async Task The_database_refuses_an_order_code_outside_the_range_PayOS_accepts(long orderCode)
    {
        var invoice = await InvoiceAsync("range", "month");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute($"INSERT INTO billing.payment_links (invoice_id, order_code) VALUES ('{invoice}', {orderCode})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_paid_invoice_and_a_USD_invoice_get_no_link_and_PayOS_gets_no_request()
    {
        var token = TestContext.Current.CancellationToken;
        var paid = await InvoiceAsync("paid", "month");
        await Execute($"UPDATE billing.invoices SET status = 'paid' WHERE id = '{paid}'");
        var voided = await InvoiceAsync("void", "month");
        await Execute($"UPDATE billing.invoices SET status = 'void' WHERE id = '{voided}'");
        var usd = await InvoiceAsync("usd", "month", "USD");

        await RunJobAsync(token);

        Assert.Equal(3, await ScalarAsync<long>("SELECT count(*) FROM billing.invoices"));
        Assert.Equal("USD", await ScalarAsync<string>($"SELECT currency FROM billing.invoices WHERE id = '{usd}'"));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.payment_links"));
        Assert.Empty(_payOs.Requests);
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'paymentLink.created'"));
    }

    // Runs the job once, at the fixed clock.
    private Task RunJobAsync(CancellationToken token) =>
        new PaymentLinks(_dataSource, _payOs, new FixedClock(_now), NullLogger<PaymentLinks>.Instance)
            .RunAsync(new ClaimedJob(Guid.CreateVersion7(), PaymentLinks.JobKind, [], 1), token);

    // The period invoice of a new subscription, in its own transaction. The subscription pins the currency before the invoice is made.
    private async Task<Guid> InvoiceAsync(string tenant, string period, string currency = "VND")
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await BillingSeed.SubscriptionAsync(_dataSource, tenant, period, token);
        if (currency != "VND")
        {
            await Execute($"UPDATE control_plane.subscriptions SET currency = '{currency}' WHERE id = '{subscription}'");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var invoice = await Invoices.CreatePeriodAsync(transaction, subscription, _periodStart, _catalogue, _now, token);
        await transaction.CommitAsync(token);
        return invoice.Id;
    }

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
