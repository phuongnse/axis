namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane:PayOs</c> settings: the PayOS adapter that creates the payment links of the <c>VND</c> invoices, see the settings table in
/// <c>docs/reference/control-plane.md#settings</c> and <c>docs/reference/payments.md#paying-in-vietnam</c>. All of them are optional.
/// </summary>
public sealed class ControlPlanePayOsOptions
{
    /// <summary><c>Fake</c>. Empty registers no PayOS adapter and no payment link job.</summary>
    public string? Provider { get; set; }
}

/// <summary>The values of <see cref="ControlPlanePayOsOptions.Provider"/>.</summary>
internal static class PayOsProviders
{
    public const string Fake = "Fake";
}
