namespace Axis.ControlPlane.Billing;

/// <summary>
/// A row of <c>billing.payment_links</c>: the PayOS payment link of an invoice, see <c>docs/reference/payments.md#paying-in-vietnam</c>. The row
/// holds no amount, because only the ledger holds money. <see cref="OrderCode"/> comes from the sequence <c>billing.payment_order_codes</c> and is
/// saved before PayOS is called, so the details and <see cref="CreatedAt"/> stay empty until PayOS has answered.
/// </summary>
public sealed class PaymentLinkRow
{
    /// <summary>The largest whole number that PayOS accepts as an <c>orderCode</c>: the largest safe integer of JavaScript.</summary>
    public const long MaxOrderCode = 9007199254740991;

    public Guid InvoiceId { get; set; }

    public long OrderCode { get; set; }

    public string? PaymentLinkId { get; set; }

    public string? CheckoutUrl { get; set; }

    public string? QrCode { get; set; }

    public string? Bin { get; set; }

    public string? AccountNumber { get; set; }

    public string? AccountName { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset ReservedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
