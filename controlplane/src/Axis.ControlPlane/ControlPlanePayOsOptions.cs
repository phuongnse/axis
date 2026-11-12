namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane:PayOs</c> settings: the PayOS adapter that creates the payment links of the <c>VND</c> invoices, see the settings table in
/// <c>docs/reference/control-plane.md#settings</c> and <c>docs/reference/payments.md#paying-in-vietnam</c>. All of them are optional,
/// and with <c>PayOs</c> as the provider the base URL and the three keys are required.
/// </summary>
public sealed class ControlPlanePayOsOptions
{
    /// <summary><c>PayOs</c> or <c>Fake</c>. Empty registers no PayOS adapter and no payment link job.</summary>
    public string? Provider { get; set; }

    /// <summary>The https address of the PayOS API, such as <c>https://api-merchant.payos.vn</c>. Required with <c>PayOs</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The PayOS client id. It comes from the key-per-file secret <c>ControlPlane__PayOs__ClientId</c>. Required with <c>PayOs</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>The PayOS API key. It comes from the key-per-file secret <c>ControlPlane__PayOs__ApiKey</c>. Required with <c>PayOs</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The PayOS checksum key that signs the requests and the answers. It comes from the key-per-file secret <c>ControlPlane__PayOs__ChecksumKey</c>. Required with <c>PayOs</c>.</summary>
    public string? ChecksumKey { get; set; }
}

/// <summary>The values of <see cref="ControlPlanePayOsOptions.Provider"/>.</summary>
internal static class PayOsProviders
{
    public const string Fake = "Fake";

    public const string PayOs = "PayOs";
}
