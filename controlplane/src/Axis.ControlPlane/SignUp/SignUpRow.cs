namespace Axis.ControlPlane.SignUp;

/// <summary>
/// A row of <c>control_plane.sign_ups</c>: one sign-up with the fields of the form, see <c>docs/reference/sign-up.md#verifying-the-email-first</c>.
/// <see cref="State"/> is <c>pending</c>, <c>verified</c> or <c>expired</c>. The confirm route sets <c>verified</c>. Only a pending sign-up holds its subdomain, and one
/// subdomain has at most one pending sign-up. <see cref="TokenHash"/> is the SHA-256 of the confirm link's token, never the token.
/// </summary>
public sealed class SignUpRow
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    public string BusinessName { get; set; } = "";

    public string Subdomain { get; set; } = "";

    /// <summary><c>en</c> or <c>vi</c>.</summary>
    public string Language { get; set; } = "en";

    /// <summary>The id of the built-in application to start from, or an empty text for a blank one.</summary>
    public string Application { get; set; } = "";

    public string State { get; set; } = "pending";

    /// <summary>The 32 bytes of the SHA-256 of the token, or null before the confirm email is made.</summary>
    public byte[]? TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a pending sign-up stops holding its subdomain: 24 hours after it was stored.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
