namespace Axis.ControlPlane.SignUp;

/// <summary>
/// A row of <c>control_plane.sign_ups</c>: one sign-up with the fields of the form, see <c>docs/reference/sign-up.md#verifying-the-email-first</c>.
/// <see cref="State"/> is <c>pending</c>, <c>verified</c> or <c>expired</c>. The confirm route sets <c>verified</c>. Only a pending sign-up holds its subdomain, and one
/// subdomain has at most one pending sign-up, and one <see cref="NormalisedEmail"/> has at most one verified sign-up. <see cref="TokenHash"/> is the SHA-256 of the confirm link's token, never the token.
/// <see cref="SecretHash"/> is the SHA-256 of the progress secret that the confirm answers, never the secret, and <see cref="Provisioning"/> is how far
/// the tenant's creation has come, see <c>docs/reference/sign-up.md#provisioning</c>.
/// </summary>
public sealed class SignUpRow
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    /// <summary>The email in lower case and without its <c>+tag</c>, see <see cref="SignUpEmail.Normalise"/>.</summary>
    public string NormalisedEmail { get; set; } = "";

    public string BusinessName { get; set; } = "";

    public string Subdomain { get; set; } = "";

    /// <summary><c>en</c> or <c>vi</c>.</summary>
    public string Language { get; set; } = "en";

    /// <summary>The id of the built-in application to start from, or an empty text for a blank one.</summary>
    public string Application { get; set; } = "";

    /// <summary>The version of the terms of service the person saw, see <c>docs/reference/sign-up.md#the-documents</c>.</summary>
    public string TermsVersion { get; set; } = "";

    /// <summary>The version of the data processing agreement the person saw.</summary>
    public string DpaVersion { get; set; } = "";

    /// <summary>The version of the privacy policy the person saw.</summary>
    public string PrivacyVersion { get; set; } = "";

    public string State { get; set; } = "pending";

    /// <summary>The 32 bytes of the SHA-256 of the token, or null before the confirm email is made.</summary>
    public byte[]? TokenHash { get; set; }

    /// <summary>The 32 bytes of the SHA-256 of the progress secret, or null until the confirm makes it.</summary>
    public byte[]? SecretHash { get; set; }

    /// <summary>
    /// <c>queued</c> (no cell had room), <c>creating</c>, <c>ready</c> or <c>delayed</c> (the cell's create job ended failed) once a verified
    /// sign-up is being provisioned, otherwise null. It is not part of <see cref="State"/>, as the one-trial-for-each-email index and the trial-used check read <c>verified</c>.
    /// </summary>
    public string? Provisioning { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a pending sign-up stops holding its subdomain: 24 hours after it was stored.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
