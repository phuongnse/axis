namespace Axis.ControlPlane.Billing;

/// <summary>
/// A row of <c>billing.ledger_entries</c>: one charge, credit, payment, refund or proration of a subscription. <see cref="Kind"/>
/// is the lowercase kind and <see cref="AmountMinor"/> is a signed whole number of minor units of <see cref="Currency"/>. The table
/// is append-only: the database rejects every update, delete and truncate.
/// </summary>
public sealed class LedgerEntryRow
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid? InvoiceId { get; set; }

    public string Kind { get; set; } = "";

    public long AmountMinor { get; set; }

    public string Currency { get; set; } = "";

    public string? ProviderReference { get; set; }

    public string IdempotencyKey { get; set; } = "";

    public DateTimeOffset OccurredAt { get; set; }
}
