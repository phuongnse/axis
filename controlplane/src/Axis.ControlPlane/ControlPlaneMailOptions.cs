namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane:Mail</c> settings: the SMTP relay that sends the control plane's emails, see the settings table in
/// <c>docs/reference/control-plane.md#operators</c>. The password comes from a secret and a message never holds it.
/// </summary>
public sealed class ControlPlaneMailOptions
{
    /// <summary>The host of the relay.</summary>
    public string? Host { get; set; }

    /// <summary>The SMTP port of the relay.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether the connection uses TLS. Only Development and Testing may turn it off.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>The user name to sign in with. Empty when the relay needs no sign-in.</summary>
    public string? UserName { get; set; }

    /// <summary>The password of <see cref="UserName"/>, from the secret <c>ControlPlane__Mail__Password</c>.</summary>
    public string? Password { get; set; }
}
