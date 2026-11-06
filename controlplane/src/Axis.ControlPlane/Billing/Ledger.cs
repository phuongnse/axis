using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// An entry to append. <see cref="AmountMinor"/> is a whole number of minor units: a positive amount raises what the subscription
/// owes and a negative one lowers it. Charges and refunds are positive, payments and credits negative, prorations never zero.
/// </summary>
internal sealed record LedgerEntry(
    Guid SubscriptionId,
    Guid? InvoiceId,
    LedgerEntryKind Kind,
    long AmountMinor,
    string Currency,
    string? ProviderReference,
    string IdempotencyKey);

/// <summary>What a subscription owes in one currency, in minor units. A negative amount means it is in credit.</summary>
internal sealed record LedgerBalance(string Currency, long AmountMinor);

/// <summary>
/// Appends to and sums <c>billing.ledger_entries</c>. There is no update or delete method, and the database rejects both: a
/// correction is a new entry. A positive amount raises what the subscription owes and a negative one lowers it.
/// </summary>
internal static class Ledger
{
    /// <summary>
    /// Appends <paramref name="entry"/> in the transaction of the change it belongs to. It returns <see langword="false"/> and adds
    /// nothing when an entry with the same idempotency key exists, even if the amount differs, so a retry never counts money twice.
    /// </summary>
    public static async Task<bool> AppendAsync(NpgsqlTransaction transaction, LedgerEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entry);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO billing.ledger_entries
                (id, subscription_id, invoice_id, kind, amount_minor, currency, provider_reference, idempotency_key)
            VALUES (@id, @subscription, @invoice, @kind, @amount, @currency, @reference, @key)
            ON CONFLICT (idempotency_key) DO NOTHING
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("subscription", entry.SubscriptionId);
        command.Parameters.AddWithValue("invoice", (object?)entry.InvoiceId ?? DBNull.Value);
        command.Parameters.AddWithValue("kind", entry.Kind.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("amount", entry.AmountMinor);
        command.Parameters.AddWithValue("currency", entry.Currency);
        command.Parameters.AddWithValue("reference", (object?)entry.ProviderReference ?? DBNull.Value);
        command.Parameters.AddWithValue("key", entry.IdempotencyKey);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Sums the entries of <paramref name="subscriptionId"/> for each currency, ordered by currency. The sum is cast back to
    /// <c>bigint</c>, so a balance beyond 64 bits fails and is never rounded.
    /// </summary>
    public static async Task<IReadOnlyList<LedgerBalance>> BalanceAsync(NpgsqlConnection connection, Guid subscriptionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            """
            SELECT currency, SUM(amount_minor)::bigint FROM billing.ledger_entries
            WHERE subscription_id = @subscription GROUP BY currency ORDER BY currency
            """,
            connection);
        command.Parameters.AddWithValue("subscription", subscriptionId);
        var balances = new List<LedgerBalance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            balances.Add(new LedgerBalance(reader.GetString(0), reader.GetInt64(1)));
        }

        return balances;
    }
}
