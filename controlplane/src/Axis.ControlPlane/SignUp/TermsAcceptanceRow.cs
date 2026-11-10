using System.Net;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// A row of <c>control_plane.terms_acceptances</c>: one acceptance of one legal document, with the version the person saw, the
/// privacy policy version, and the client address and user agent of the request. <see cref="Document"/> is <c>terms</c> or <c>dpa</c>.
/// The table is append-only: the database rejects every update, delete and truncate, see <c>docs/reference/sign-up.md#terms-acceptance</c>.
/// </summary>
public sealed class TermsAcceptanceRow
{
    public Guid Id { get; set; }

    public string Account { get; set; } = "";

    /// <summary>The subdomain of the tenant.</summary>
    public string TenantId { get; set; } = "";

    public string Document { get; set; } = "";

    public string Version { get; set; } = "";

    public string PrivacyVersion { get; set; } = "";

    public DateTimeOffset AcceptedAt { get; set; }

    public IPAddress? ClientAddress { get; set; }

    public string? UserAgent { get; set; }
}
