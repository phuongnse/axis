namespace Axis.ControlPlane.Billing;

/// <summary>
/// Creates a PayOS payment request, see <c>docs/reference/payments.md#paying-in-vietnam</c>. The <see cref="PayOsPaymentRequest.OrderCode"/> is
/// the caller's unique whole number, and the caller keeps it, so a retry sends the same one.
/// </summary>
internal interface IPayOs
{
    /// <summary>Creates the payment request and returns its payment link with the VietQR code and the transfer details.</summary>
    /// <exception cref="PayOsException">PayOS refused or did not answer the request.</exception>
    Task<PayOsPaymentLink> CreatePaymentLinkAsync(PayOsPaymentRequest request, CancellationToken cancellationToken);
}

/// <summary>A request for a payment link: the unique <paramref name="OrderCode"/>, the <paramref name="Amount"/> in <c>VND</c> and the transfer's <paramref name="Description"/>.</summary>
internal sealed record PayOsPaymentRequest(long OrderCode, long Amount, string Description);

/// <summary>What PayOS answers: the link's ids and the VietQR code with the transfer details. It holds no amount.</summary>
internal sealed record PayOsPaymentLink(string PaymentLinkId, string CheckoutUrl, string QrCode, string Bin, string AccountNumber, string AccountName, string Description);

/// <summary>A PayOS request failed. The message never holds a key.</summary>
internal sealed class PayOsException(string message) : Exception(message);
