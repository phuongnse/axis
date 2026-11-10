using System.Text;
using Axis.ControlPlane.Billing;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// Billing invoices against a real PostgreSQL, see <c>docs/reference/payments.md#invoices</c>: the lines of a pinned plan and its add-ons, the
/// <c>charge</c> entry of each line, the amount that the ledger holds, one invoice for each period and the due dates.
/// </summary>
public sealed class InvoiceTests : IAsyncLifetime
{
    private const long PlanVnd = 4_900_000;
    private const long PackVnd = 500_000;

    private static readonly DateTimeOffset _now = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _periodStart = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = Catalogue();
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
    public async Task An_invoice_has_a_plan_line_and_a_line_for_each_add_on_and_a_charge_for_each_line()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("lines", "month");
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{subscription}', 'users-10', 2)");

        var invoice = await CreatePeriodAsync(subscription);

        Assert.True(invoice.Created);
        Assert.Equal("period", await ScalarAsync<string>($"SELECT kind FROM billing.invoices WHERE id = '{invoice.Id}'"));
        Assert.Equal("open", await ScalarAsync<string>($"SELECT status FROM billing.invoices WHERE id = '{invoice.Id}'"));
        Assert.Equal(_periodStart, await ScalarAsync<DateTime>($"SELECT due_at FROM billing.invoices WHERE id = '{invoice.Id}'"));
        Assert.Equal(
            "1 plan team 1|2 addOn users-10 2",
            await ScalarAsync<string>($"SELECT string_agg(line_number || ' ' || kind || ' ' || code || ' ' || quantity, '|' ORDER BY line_number) FROM billing.invoice_lines WHERE invoice_id = '{invoice.Id}'"));
        Assert.Equal(
            $"charge {PlanVnd} invoice:{invoice.Id}:1|charge {2 * PackVnd} invoice:{invoice.Id}:2",
            await ScalarAsync<string>($"SELECT string_agg(kind || ' ' || amount_minor || ' ' || idempotency_key, '|' ORDER BY idempotency_key) FROM billing.ledger_entries WHERE invoice_id = '{invoice.Id}'"));
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        Assert.Equal(PlanVnd + (2 * PackVnd), await Invoices.AmountAsync(connection, invoice.Id, token));
    }

    [Fact]
    public async Task The_amount_of_a_yearly_invoice_is_ten_times_the_monthly_price()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("yearly", "year");

        var invoice = await CreatePeriodAsync(subscription);

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        Assert.Equal(10 * PlanVnd, await Invoices.AmountAsync(connection, invoice.Id, token));
    }

    [Fact]
    public async Task The_amount_leaves_out_payments_and_refunds()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("paid", "month");
        var invoice = await CreatePeriodAsync(subscription);
        await Execute($"""
            INSERT INTO billing.ledger_entries (id, subscription_id, invoice_id, kind, amount_minor, currency, idempotency_key)
            VALUES ('{Guid.CreateVersion7()}', '{subscription}', '{invoice.Id}', 'payment', {-PlanVnd}, 'VND', 'payment:{invoice.Id}')
            """);

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        Assert.Equal(PlanVnd, await Invoices.AmountAsync(connection, invoice.Id, token));
    }

    [Fact]
    public async Task Creating_the_invoice_of_a_period_twice_leaves_one_invoice_and_no_new_ledger_entry()
    {
        var subscription = await SeedAsync("twice", "month");
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{subscription}', 'users-10', 1)");

        var first = await CreatePeriodAsync(subscription);
        var entries = await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries");
        var second = await CreatePeriodAsync(subscription);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await ScalarAsync<long>($"SELECT count(*) FROM billing.invoices WHERE subscription_id = '{subscription}'"));
        Assert.Equal(2, await ScalarAsync<long>($"SELECT count(*) FROM billing.invoice_lines WHERE invoice_id = '{first.Id}'"));
        Assert.Equal(entries, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries"));
    }

    [Fact]
    public async Task A_change_invoice_is_due_seven_days_after_it_is_created()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("change", "month");
        CreatedInvoice invoice;
        await using (var connection = await _dataSource.OpenConnectionAsync(token))
        await using (var transaction = await connection.BeginTransactionAsync(token))
        {
            invoice = await Invoices.CreateChangeAsync(transaction, subscription, _catalogue, _now, token);
            await transaction.CommitAsync(token);
        }

        Assert.Equal("change", await ScalarAsync<string>($"SELECT kind FROM billing.invoices WHERE id = '{invoice.Id}'"));
        Assert.Equal(TimeSpan.FromDays(7), await ScalarAsync<TimeSpan>($"SELECT due_at - created_at FROM billing.invoices WHERE id = '{invoice.Id}'"));
        Assert.Equal(_now, await ScalarAsync<DateTime>($"SELECT created_at FROM billing.invoices WHERE id = '{invoice.Id}'"));
        Assert.True(await ScalarAsync<bool>($"SELECT period_start IS NULL FROM billing.invoices WHERE id = '{invoice.Id}'"));
    }

    [Fact]
    public async Task An_add_on_the_pinned_plan_does_not_list_fails_and_writes_nothing()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = await SeedAsync("unlisted", "month");
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{subscription}', 'storage-1', 1)");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreatePeriodAsync(subscription));

        Assert.Contains("storage-1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.invoices"));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries"));
    }

    [Fact]
    public async Task A_subscription_with_no_pinned_plan_has_nothing_to_invoice()
    {
        var subscription = await SeedAsync("unpinned", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreatePeriodAsync(subscription));

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM billing.invoices"));
    }

    // Creates the invoice of the period in its own transaction.
    private async Task<CreatedInvoice> CreatePeriodAsync(Guid subscription)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var invoice = await Invoices.CreatePeriodAsync(transaction, subscription, _periodStart, _catalogue, _now, token);
        await transaction.CommitAsync(token);
        return invoice;
    }

    private static PlanCatalogue Catalogue()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""
                { "ControlPlane": { "plans": [{
                  "code": "team",
                  "version": 1,
                  "name": { "en": "Team", "vi": "Nhóm" },
                  "includedUsers": 25,
                  "quotas": {
                    "storage": 10737418240,
                    "applications": 5,
                    "emailsPerDay": 2000,
                    "concurrentImports": 2,
                    "concurrentConnectorCalls": 10,
                    "concurrentQueries": 10,
                    "workerWeight": 2,
                    "authoringRequests": 200
                  },
                  "prices": [{ "currency": "VND", "month": 4900000 }, { "currency": "USD", "month": 1900 }],
                  "addOns": [{ "code": "users-10", "users": 10, "prices": [{ "currency": "VND", "month": 500000 }, { "currency": "USD", "month": 20 }] }]
                }] } }
                """)))
            .Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return PlanCatalogue.Create(options.Plans);
    }

    // A subscription of a verified sign-up that pins the team plan in VND for the period, or none when the period is null.
    private async Task<Guid> SeedAsync(string tenant, string? period)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        await Execute($"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, trial_ends_at, step_id, state{(period is null ? "" : ", plan_code, plan_version, currency, period")})
            VALUES ('{id}', '{tenant}', '{signUp}', now(), '{Guid.CreateVersion7()}', 'trialing'{(period is null ? "" : $", 'team', 1, 'VND', '{period}'")})
            """);
        return id;
    }

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);
}
