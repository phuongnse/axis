namespace Axis.ControlPlane.SignUp;

/// <summary>
/// A row of <c>control_plane.sign_up_emails</c>: one sign-up email that was queued for an address, see
/// <c>docs/reference/sign-up.md#bots-and-abuse</c>. The table keeps the SHA-256 of the normalised address and no plain address, and the
/// control plane counts the rows of the last 24 hours. The <c>expireSignUps</c> job deletes the rows older than that.
/// </summary>
public sealed class SignUpEmailRow
{
    public Guid Id { get; set; }

    /// <summary>The 32 bytes of the SHA-256 of the normalised address.</summary>
    public byte[] AddressHash { get; set; } = [];

    /// <summary>When the email was queued.</summary>
    public DateTimeOffset QueuedAt { get; set; }
}
