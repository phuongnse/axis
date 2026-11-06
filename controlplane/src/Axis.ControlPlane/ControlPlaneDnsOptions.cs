namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane:Dns</c> settings: the DNS server that holds the tenant subdomains, see the settings table in
/// <c>docs/reference/control-plane.md#settings</c>. All of them are optional until a provider is named. The key comes from a
/// secret and a message never holds it.
/// </summary>
public sealed class ControlPlaneDnsOptions
{
    /// <summary><c>InMemory</c> or <c>Rfc2136</c>. Empty registers no DNS adapter.</summary>
    public string? Provider { get; set; }

    /// <summary>The host of the DNS server that takes the dynamic updates.</summary>
    public string? Server { get; set; }

    /// <summary>The TCP port of the DNS server.</summary>
    public int Port { get; set; } = 53;

    /// <summary>The zone that holds the tenant subdomains, such as <c>vn.example.com</c>.</summary>
    public string? Zone { get; set; }

    /// <summary>The name of the TSIG key that the server knows, such as <c>axis-controlplane</c>.</summary>
    public string? KeyName { get; set; }

    /// <summary>The TSIG HMAC-SHA256 key as base64, from the secret <c>ControlPlane__Dns__Key</c>.</summary>
    public string? Key { get; set; }
}

/// <summary>The values of <see cref="ControlPlaneDnsOptions.Provider"/>.</summary>
internal static class DnsProviders
{
    public const string InMemory = "InMemory";

    public const string Rfc2136 = "Rfc2136";
}
