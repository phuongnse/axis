using System.Globalization;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.Subscriptions;
using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>An invoice that a create call returns. <see cref="Created"/> is <see langword="false"/> when the period already had it.</summary>
internal sealed record CreatedInvoice(Guid Id, bool Created);

/// <summary>
/// Creates billing invoices and reads their amount, see <c>docs/reference/payments.md#invoices</c>. An invoice takes its lines from the plan and
/// the add-ons that the subscription pins, see <c>docs/reference/plans.md#the-catalogue</c>: the plan is line 1 and each add-on follows, ordered by
/// code. Each line is stored with no amount and writes one <c>charge</c> ledger entry in the same transaction, so the ledger stays the only
/// place that holds money. The calls write no audit record: the job or command that calls them does.
/// </summary>
internal static class Invoices
{
    /// <summary>A change invoice is due this many days after it is created.</summary>
    private const int ChangeDueDays = 7;

    /// <summary>
    /// Creates the invoice of the period that starts at <paramref name="periodStart"/>, due on that day. A period that already has an invoice
    /// returns it with <see cref="CreatedInvoice.Created"/> false and writes no line or ledger entry, so a retried job or a race never charges twice.
    /// </summary>
    /// <exception cref="InvalidOperationException">The subscription pins no plan, currency or period, or holds an add-on its plan does not list.</exception>
    public static Task<CreatedInvoice> CreatePeriodAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        DateTimeOffset periodStart,
        PlanCatalogue catalogue,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        CreateAsync(transaction, subscriptionId, "period", periodStart, periodStart, catalogue, now, cancellationToken);

    /// <summary>
    /// Creates the invoice of a plan change or an add-on, due 7 days after <paramref name="now"/>. For now it charges the whole pinned plan and
    /// add-ons, as the proration of a change is not built.
    /// </summary>
    /// <exception cref="InvalidOperationException">The subscription pins no plan, currency or period, or holds an add-on its plan does not list.</exception>
    public static Task<CreatedInvoice> CreateChangeAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        PlanCatalogue catalogue,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        CreateAsync(transaction, subscriptionId, "change", null, now.AddDays(ChangeDueDays), catalogue, now, cancellationToken);

    /// <summary>
    /// The amount of the invoice: the sum of the ledger entries with its id, without the <c>payment</c> and <c>refund</c> entries, because the
    /// payments against an invoice are compared with its amount and a plain sum would fall to zero once it is paid.
    /// </summary>
    public static async Task<long> AmountAsync(NpgsqlConnection connection, Guid invoiceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(SUM(amount_minor), 0)::bigint FROM billing.ledger_entries WHERE invoice_id = @invoice AND kind NOT IN ('payment', 'refund')",
            connection);
        command.Parameters.AddWithValue("invoice", invoiceId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<CreatedInvoice> CreateAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        string kind,
        DateTimeOffset? periodStart,
        DateTimeOffset dueAt,
        PlanCatalogue catalogue,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(catalogue);
        var connection = transaction.Connection!;
        if (await SubscriptionPlans.ReadAsync(connection, transaction, subscriptionId, catalogue, cancellationToken) is not { Currency: { } currency, Period: { } period } pinned)
        {
            throw new InvalidOperationException($"The subscription {subscriptionId} pins no plan with a currency and a period, so it has nothing to invoice.");
        }

        var addOns = await SubscriptionPlans.ReadAddOnsAsync(connection, transaction, subscriptionId, pinned.Plan, cancellationToken);
        var invoiceId = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand(
            $"""
            INSERT INTO billing.invoices
                (id, subscription_id, kind, status, currency, plan_code, plan_version, period, period_start, due_at, created_at)
            VALUES (@id, @subscription, @kind, 'open', @currency, @plan, @version, @period, @start, @due, @now)
            {(periodStart is null ? "" : "ON CONFLICT (subscription_id, period_start) WHERE kind = 'period' DO NOTHING")}
            RETURNING id
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("id", invoiceId);
            insert.Parameters.AddWithValue("subscription", subscriptionId);
            insert.Parameters.AddWithValue("kind", kind);
            insert.Parameters.AddWithValue("currency", currency);
            insert.Parameters.AddWithValue("plan", pinned.Plan.Code);
            insert.Parameters.AddWithValue("version", pinned.Plan.Version);
            insert.Parameters.AddWithValue("period", period);
            insert.Parameters.AddWithValue("start", (object?)periodStart ?? DBNull.Value);
            insert.Parameters.AddWithValue("due", dueAt);
            insert.Parameters.AddWithValue("now", now);
            if (await insert.ExecuteScalarAsync(cancellationToken) is null)
            {
                return new CreatedInvoice(await ExistingPeriodAsync(connection, transaction, subscriptionId, periodStart!.Value, cancellationToken), false);
            }
        }

        var lines = new List<(string Kind, string Code, int Quantity, long Amount)> { ("plan", pinned.Plan.Code, 1, pinned.Plan.Price(currency, period)) };
        lines.AddRange(addOns.Select(held => ("addOn", held.AddOn.Code, held.Quantity, checked(held.AddOn.Price(currency, period) * held.Quantity))));
        for (var index = 0; index < lines.Count; index++)
        {
            var (lineKind, code, quantity, amount) = lines[index];
            var lineNumber = index + 1;
            await using (var line = new NpgsqlCommand(
                "INSERT INTO billing.invoice_lines (invoice_id, line_number, kind, code, quantity) VALUES (@invoice, @number, @kind, @code, @quantity)",
                connection,
                transaction))
            {
                line.Parameters.AddWithValue("invoice", invoiceId);
                line.Parameters.AddWithValue("number", lineNumber);
                line.Parameters.AddWithValue("kind", lineKind);
                line.Parameters.AddWithValue("code", code);
                line.Parameters.AddWithValue("quantity", quantity);
                await line.ExecuteNonQueryAsync(cancellationToken);
            }

            await Ledger.AppendAsync(
                transaction,
                new LedgerEntry(subscriptionId, invoiceId, LedgerEntryKind.Charge, amount, currency, null, $"invoice:{invoiceId}:{lineNumber.ToString(CultureInfo.InvariantCulture)}"),
                cancellationToken);
        }

        return new CreatedInvoice(invoiceId, true);
    }

    private static async Task<Guid> ExistingPeriodAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subscriptionId, DateTimeOffset periodStart, CancellationToken cancellationToken)
    {
        await using var select = new NpgsqlCommand(
            "SELECT id FROM billing.invoices WHERE subscription_id = @subscription AND kind = 'period' AND period_start = @start",
            connection,
            transaction);
        select.Parameters.AddWithValue("subscription", subscriptionId);
        select.Parameters.AddWithValue("start", periodStart);
        return (Guid)(await select.ExecuteScalarAsync(cancellationToken))!;
    }
}
