namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane:Billing</c> settings: the identity provider and the client that the operator's billing system signs in with, see the settings
/// table in <c>docs/reference/control-plane.md#settings</c> and <c>docs/reference/billing-api.md#signing-in</c>. All of them are optional, and once one
/// is set all are required. Without them every billing route answers <c>401</c>.
/// </summary>
public sealed class ControlPlaneBillingOptions
{
    /// <summary>The issuer URL of the operator's identity provider. Must be an absolute <c>https</c> URL.</summary>
    public string? Authority { get; set; }

    /// <summary>The audience that the billing client's token carries.</summary>
    public string? Audience { get; set; }

    /// <summary>The id of the billing client, which the token's <c>client_id</c> or <c>azp</c> claim names.</summary>
    public string? ClientId { get; set; }

    internal bool IsSet =>
        !string.IsNullOrWhiteSpace(Authority) || !string.IsNullOrWhiteSpace(Audience) || !string.IsNullOrWhiteSpace(ClientId);

    internal bool IsComplete =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(Audience) && !string.IsNullOrWhiteSpace(ClientId);
}
