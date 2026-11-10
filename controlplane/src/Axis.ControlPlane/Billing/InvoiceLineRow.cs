namespace Axis.ControlPlane.Billing;

/// <summary>
/// A row of <c>billing.invoice_lines</c>: one line of an invoice, see <c>docs/reference/payments.md#invoices</c>. The key is the invoice and
/// the <see cref="LineNumber"/>, which starts at 1 with the plan. <see cref="Kind"/> is <c>plan</c> or <c>addOn</c>, <see cref="Code"/> is the
/// plan or add-on code and <see cref="Quantity"/> is how many packs. The row holds no amount: the ledger holds the line's charge.
/// </summary>
public sealed class InvoiceLineRow
{
    public Guid InvoiceId { get; set; }

    public int LineNumber { get; set; }

    public string Kind { get; set; } = "";

    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}
