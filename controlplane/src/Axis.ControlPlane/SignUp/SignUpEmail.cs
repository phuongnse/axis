namespace Axis.ControlPlane.SignUp;

/// <summary>The rule that compares sign-up emails, see <c>docs/reference/sign-up.md#bots-and-abuse</c>.</summary>
internal static class SignUpEmail
{
    /// <summary>
    /// The address in lower case, with the <c>+tag</c> cut from the part before the last <c>@</c>, so <c>Owner+Test@Example.com</c>
    /// and <c>owner@example.com</c> are one address.
    /// </summary>
    public static string Normalise(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var at = email.LastIndexOf('@');
        var local = at < 0 ? email : email[..at];
        var tag = local.IndexOf('+', StringComparison.Ordinal);
        if (tag >= 0)
        {
            local = local[..tag];
        }

        return (at < 0 ? local : local + email[at..]).ToLowerInvariant();
    }
}
