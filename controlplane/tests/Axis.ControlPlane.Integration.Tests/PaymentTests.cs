using Axis.ControlPlane.Billing;
using Axis.ControlPlane.Plans;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// Payments against billing invoices on a real PostgreSQL, see <c>docs/reference/payments.md#invoices</c>: settling oldest first, the amount
/// due, an overpayment's credit and the fixed currency.
/// </summary>
public sealed class PaymentTests : IAsyncLifetime
{
    private const long PlanVnd = 4_900_000;

    private static readonly DateTimeOffset _now = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _firstPeriod = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _secondPeriod = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = BillingSeed.Catalogue();
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
    public async Task A_payment_smaller_than_the_invoice_leaves_it_open_with_the_rest_due()
    {
        var subscription = await SeedAsync("partial");
        var invoice = await CreateAsync(subscription, _firstPeriod);

        Assert.True(await PayAsync(invoice.Id, PlanVnd - 1, "VND", "partial:1"));

        Assert.Equal("open", await StatusAsync(invoice.Id));
        Assert.Equal(1, await AmountDueAsync(invoice.Id));
    }

    [Fact]
    public async Task A_payment_that_covers_the_invoice_marks_it_paid_with_nothing_due()
    {
        var subscription = await SeedAsync("exact");
        var invoice = await CreateAsync(subscription, _firstPeriod);

        await PayAsync(invoice.Id, PlanVnd, "VND", "exact:1");

        Assert.Equal("paid", await StatusAsync(invoice.Id));
        Assert.Equal(0, await AmountDueAsync(invoice.Id));
    }

    [Fact]
    public async Task An_overpayment_marks_the_invoice_paid_and_its_credit_lowers_the_next_invoice()
    {
        var subscription = await SeedAsync("overpaid");
        var first = await CreateAsync(subscription, _firstPeriod);
        var second = await CreateAsync(subscription, _secondPeriod);

        await PayAsync(first.Id, PlanVnd + 1_000_000, "VND", "overpaid:1");

        Assert.Equal("paid", await StatusAsync(first.Id));
        Assert.Equal("open", await StatusAsync(second.Id));
        Assert.Equal(PlanVnd - 1_000_000, await AmountDueAsync(second.Id));
    }

    [Fact]
    public async Task An_invoice_created_while_the_credit_covers_it_is_paid_at_once()
    {
        var subscription = await SeedAsync("credit");
        var first = await CreateAsync(subscription, _firstPeriod);
        await PayAsync(first.Id, PlanVnd + PlanVnd + 1, "VND", "credit:1");

        var second = await CreateAsync(subscription, _secondPeriod);

        Assert.Equal("paid", await StatusAsync(second.Id));
        Assert.Equal(0, await AmountDueAsync(second.Id));
    }

    [Fact]
    public async Task A_repeated_idempotency_key_adds_no_second_payment()
    {
        var subscription = await SeedAsync("repeat");
        var invoice = await CreateAsync(subscription, _firstPeriod);

        Assert.True(await PayAsync(invoice.Id, 100, "VND", "repeat:1"));
        Assert.False(await PayAsync(invoice.Id, 100, "VND", "repeat:1"));

        Assert.Equal(1, await ScalarAsync<long>($"SELECT count(*) FROM billing.ledger_entries WHERE invoice_id = '{invoice.Id}' AND kind = 'payment'"));
        Assert.Equal(PlanVnd - 100, await AmountDueAsync(invoice.Id));
    }

    [Fact]
    public async Task The_currency_of_a_subscription_changes_until_it_has_a_ledger_entry_and_then_the_database_refuses()
    {
        var subscription = await SeedAsync("currency");
        await Execute($"UPDATE control_plane.subscriptions SET currency = 'USD' WHERE id = '{subscription}'");
        Assert.Equal("USD", await ScalarAsync<string>($"SELECT currency FROM control_plane.subscriptions WHERE id = '{subscription}'"));
        await Execute($"UPDATE control_plane.subscriptions SET currency = 'VND' WHERE id = '{subscription}'");
        await CreateAsync(subscription, _firstPeriod);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => Execute($"UPDATE control_plane.subscriptions SET currency = 'USD' WHERE id = '{subscription}'"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        Assert.Equal("VND", await ScalarAsync<string>($"SELECT currency FROM control_plane.subscriptions WHERE id = '{subscription}'"));
    }

    [Fact]
    public async Task A_payment_in_another_currency_is_refused_and_writes_nothing()
    {
        var subscription = await SeedAsync("other");
        var invoice = await CreateAsync(subscription, _firstPeriod);

        await Assert.ThrowsAsync<InvalidOperationException>(() => PayAsync(invoice.Id, PlanVnd, "USD", "other:1"));

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries WHERE kind = 'payment'"));
        Assert.Equal("open", await StatusAsync(invoice.Id));
    }

    private async Task<CreatedInvoice> CreateAsync(Guid subscription, DateTimeOffset periodStart)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var invoice = await Invoices.CreatePeriodAsync(transaction, subscription, periodStart, _catalogue, _now, token);
        await transaction.CommitAsync(token);
        return invoice;
    }

    // Records the payment in its own transaction.
    private async Task<bool> PayAsync(Guid invoice, long amount, string currency, string key)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var added = await Payments.RecordAsync(transaction, invoice, amount, currency, null, key, token);
        await transaction.CommitAsync(token);
        return added;
    }

    private async Task<long> AmountDueAsync(Guid invoice)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        return await Invoices.AmountDueAsync(connection, invoice, token);
    }

    private Task<string> StatusAsync(Guid invoice) => ScalarAsync<string>($"SELECT status FROM billing.invoices WHERE id = '{invoice}'");

    private Task<Guid> SeedAsync(string tenant) => BillingSeed.SubscriptionAsync(_dataSource, tenant, "month", TestContext.Current.CancellationToken);

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);
}
