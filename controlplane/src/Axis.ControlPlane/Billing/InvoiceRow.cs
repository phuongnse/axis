namespace Axis.ControlPlane.Billing;

/// <summary>
/// A row of <c>billing.invoices</c>: what a subscription owes for a period or a change, see <c>docs/reference/payments.md#invoices</c>.
/// <see cref="Kind"/> is <c>period</c> or <c>change</c>, <see cref="Status"/> is <c>open</c>, <c>paid</c> or <c>void</c> and
/// <see cref="Currency"/> is <c>VND</c> or <c>USD</c>. The row holds no amount: the amount is the sum of the ledger entries with its id.
/// <see cref="PlanCode"/>, <see cref="PlanVersion"/> and <see cref="Period"/> record the pin that the invoice was made from, and
/// <see cref="PeriodStart"/> is set for a period invoice only.
/// </summary>
public sealed class InvoiceRow
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public string Kind { get; set; } = "";

    public string Status { get; set; } = "";

    public string Currency { get; set; } = "";

    public string PlanCode { get; set; } = "";

    public int PlanVersion { get; set; }

    public string Period { get; set; } = "";

    public DateTimeOffset? PeriodStart { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
