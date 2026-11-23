namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane:BillingEvents</c> settings: where the control plane sends the operator's billing system its signed events and the secret
/// that signs them, see the settings table in <c>docs/reference/control-plane.md#settings</c> and <c>docs/reference/billing-api.md#events</c>.
/// Both are optional. Without the address no event is written. Once the address is set, the secret is required. A secret without an address is ignored.
/// </summary>
public sealed class ControlPlaneBillingEventsOptions
{
    /// <summary>The address of the operator's billing system's receiver. Must be an absolute <c>https</c> URL.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// The signing secret. It may hold two secrets separated by a space, so the operator can rotate it, and a secret that starts with <c>whsec_</c> is
    /// followed by base64. It comes from the key-per-file secret <c>ControlPlane__BillingEvents__Secret</c>, never from a plain setting.
    /// </summary>
    public string? Secret { get; set; }

    internal bool IsSet => !string.IsNullOrWhiteSpace(Url);
}
