using Axis.ControlPlane.Billing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The ledger against a real PostgreSQL. The control plane's role owns the tables, as in production, and keeps only
/// <c>SELECT</c> and <c>INSERT</c> on the ledger. A superuser skips the grants and meets the trigger.
/// </summary>
public sealed class LedgerTests : IAsyncLifetime
{
    private static readonly string[] _changes =
    [
        "UPDATE billing.ledger_entries SET amount_minor = 1",
        "UPDATE billing.ledger_entries SET currency = 'USD' WHERE subscription_id IS NOT NULL",
        "DELETE FROM billing.ledger_entries",
        "DELETE FROM billing.ledger_entries WHERE subscription_id IS NOT NULL",
        "TRUNCATE billing.ledger_entries",
    ];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _role = null!;
    private NpgsqlDataSource _superuser = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        await using (var admin = NpgsqlDataSource.Create(_container.GetConnectionString()))
        {
            await ControlPlaneDatabase.ExecuteAsync(admin, "CREATE ROLE axis_controlplane LOGIN PASSWORD 'ledger-test'", token);
            await ControlPlaneDatabase.ExecuteAsync(admin, "CREATE DATABASE axis_controlplane OWNER axis_controlplane", token);
        }

        var superuser = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "axis_controlplane" }.ConnectionString;
        var role = new NpgsqlConnectionStringBuilder(superuser) { Username = "axis_controlplane", Password = "ledger-test" }.ConnectionString;
        await ControlPlaneDatabase.MigrateAsync(role, token);
        _role = NpgsqlDataSource.Create(role);
        _superuser = NpgsqlDataSource.Create(superuser);
    }

    public async ValueTask DisposeAsync()
    {
        await _role.DisposeAsync();
        await _superuser.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_role_that_owns_the_ledger_cannot_update_delete_or_truncate_an_entry()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = Guid.NewGuid();
        Assert.True(await AppendAsync(Entry(subscription, LedgerEntryKind.Charge, 500000, "VND")));

        foreach (var sql in _changes)
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ControlPlaneDatabase.ExecuteAsync(_role, sql, token));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }

        await AssertUnchangedAsync(subscription, "VND", 500000);
    }

    [Fact]
    public async Task A_superuser_cannot_update_delete_or_truncate_an_entry_either()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = Guid.NewGuid();
        Assert.True(await AppendAsync(Entry(subscription, LedgerEntryKind.Charge, 700, "USD")));

        foreach (var sql in _changes)
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ControlPlaneDatabase.ExecuteAsync(_superuser, sql, token));
            Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        }

        await AssertUnchangedAsync(subscription, "USD", 700);
    }

    [Fact]
    public async Task A_second_entry_with_the_same_idempotency_key_adds_nothing()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = Guid.NewGuid();
        var first = Entry(subscription, LedgerEntryKind.Charge, 1000, "VND") with { IdempotencyKey = "webhook-1" };

        Assert.True(await AppendAsync(first));
        Assert.False(await AppendAsync(first with { AmountMinor = 9999 }));

        var count = await ScalarAsync<long>($"SELECT count(*) FROM billing.ledger_entries WHERE subscription_id = '{subscription}'");
        Assert.Equal(1, count);
        await using var connection = await _role.OpenConnectionAsync(token);
        Assert.Equal([new LedgerBalance("VND", 1000)], await Ledger.BalanceAsync(connection, subscription, token));
    }

    [Fact]
    public async Task The_balance_is_the_sum_of_the_entries_of_the_subscription_for_each_currency()
    {
        var token = TestContext.Current.CancellationToken;
        var subscription = Guid.NewGuid();
        Assert.True(await AppendAsync(Entry(subscription, LedgerEntryKind.Charge, 500000, "VND")));
        Assert.True(await AppendAsync(Entry(subscription, LedgerEntryKind.Payment, -200000, "VND")));
        Assert.True(await AppendAsync(Entry(subscription, LedgerEntryKind.Proration, -10000, "VND")));
        var usd = Entry(subscription, LedgerEntryKind.Charge, 1500, "USD");
        Assert.True(await AppendAsync(usd));
        Assert.False(await AppendAsync(usd));
        Assert.True(await AppendAsync(Entry(Guid.NewGuid(), LedgerEntryKind.Charge, 42, "VND")));

        await using var connection = await _role.OpenConnectionAsync(token);
        Assert.Equal(
            [new LedgerBalance("USD", 1500), new LedgerBalance("VND", 290000)],
            await Ledger.BalanceAsync(connection, subscription, token));
        Assert.Empty(await Ledger.BalanceAsync(connection, Guid.NewGuid(), token));
    }

    [Theory]
    [InlineData("invoice", 100, "VND")]
    [InlineData("charge", -100, "VND")]
    [InlineData("charge", 0, "VND")]
    [InlineData("refund", -100, "VND")]
    [InlineData("payment", 100, "VND")]
    [InlineData("credit", 100, "VND")]
    [InlineData("proration", 0, "VND")]
    [InlineData("charge", 100, "vnd")]
    [InlineData("charge", 100, "DONG")]
    public async Task An_entry_with_an_unknown_kind_a_wrong_sign_or_a_malformed_currency_is_refused(string kind, long amount, string currency)
    {
        var token = TestContext.Current.CancellationToken;
        await using var command = _role.CreateCommand(
            """
            INSERT INTO billing.ledger_entries (id, subscription_id, kind, amount_minor, currency, idempotency_key)
            VALUES (gen_random_uuid(), gen_random_uuid(), @kind, @amount, @currency, gen_random_uuid()::text)
            """);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("currency", currency);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Theory]
    [InlineData("Charge", 1)]
    [InlineData("Refund", 1)]
    [InlineData("Payment", -1)]
    [InlineData("Credit", -1)]
    [InlineData("Proration", 1)]
    [InlineData("Proration", -1)]
    public async Task Every_kind_is_stored_in_lowercase_with_its_sign(string name, long amount)
    {
        var kind = Enum.Parse<LedgerEntryKind>(name);
        var subscription = Guid.NewGuid();
        Assert.True(await AppendAsync(Entry(subscription, kind, amount, "VND")));

        Assert.Equal(
            kind.ToString().ToLowerInvariant(),
            await ScalarAsync<string>($"SELECT kind FROM billing.ledger_entries WHERE subscription_id = '{subscription}'"));
    }

    private static LedgerEntry Entry(Guid subscription, LedgerEntryKind kind, long amount, string currency) =>
        new(subscription, null, kind, amount, currency, null, Guid.NewGuid().ToString());

    private async Task<bool> AppendAsync(LedgerEntry entry)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _role.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var added = await Ledger.AppendAsync(transaction, entry, token);
        await transaction.CommitAsync(token);
        return added;
    }

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_role, sql, TestContext.Current.CancellationToken);

    private async Task AssertUnchangedAsync(Guid subscription, string currency, long amount)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _role.OpenConnectionAsync(token);
        Assert.Equal([new LedgerBalance(currency, amount)], await Ledger.BalanceAsync(connection, subscription, token));
        Assert.True(await ScalarAsync<long>("SELECT count(*) FROM billing.ledger_entries") >= 1);
    }
}
