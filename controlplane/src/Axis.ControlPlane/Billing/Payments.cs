using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// Records payments against billing invoices, see <c>docs/reference/payments.md#invoices</c>. A payment is one <c>payment</c> ledger entry,
/// so the ledger stays the only place that holds money, and it settles the subscription's open invoices. The call writes no audit record: the
/// webhook or command that calls it does.
/// </summary>
internal static class Payments
{
    /// <summary>
    /// Appends a payment of <paramref name="amountMinor"/> against the invoice and settles the subscription's invoices, in the transaction of the
    /// change it belongs to. It returns <see langword="false"/> and adds nothing when an entry with the same idempotency key exists, so a retry
    /// never counts money twice.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The amount is not above zero.</exception>
    /// <exception cref="InvalidOperationException">There is no such invoice, or the currency is not the subscription's. Nothing is written.</exception>
    public static async Task<bool> RecordAsync(
        NpgsqlTransaction transaction,
        Guid invoiceId,
        long amountMinor,
        string currency,
        string? providerReference,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);
        Guid subscriptionId;
        string subscriptionCurrency;
        await using (var select = new NpgsqlCommand(
            """
            SELECT i.subscription_id, s.currency
            FROM billing.invoices i JOIN control_plane.subscriptions s ON s.id = i.subscription_id
            WHERE i.id = @invoice
            FOR UPDATE OF s
            """,
            transaction.Connection,
            transaction))
        {
            select.Parameters.AddWithValue("invoice", invoiceId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"There is no invoice {invoiceId}.");
            }

            subscriptionId = reader.GetGuid(0);
            subscriptionCurrency = reader.GetString(1);
        }

        if (subscriptionCurrency != currency)
        {
            throw new InvalidOperationException($"The subscription {subscriptionId} is in {subscriptionCurrency}, so it cannot take a payment in {currency}.");
        }

        var added = await Ledger.AppendAsync(
            transaction,
            new LedgerEntry(subscriptionId, invoiceId, LedgerEntryKind.Payment, -amountMinor, currency, providerReference, idempotencyKey),
            cancellationToken);
        await Invoices.SettleAsync(transaction, subscriptionId, cancellationToken);
        return added;
    }
}
