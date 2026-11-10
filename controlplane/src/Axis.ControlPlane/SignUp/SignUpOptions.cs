namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The <c>ControlPlane:SignUp</c> settings: what the sign-up page offers, see the settings table in
/// <c>docs/reference/control-plane.md#settings</c>. The applications are optional. The documents are optional as a group,
/// and once one of their settings is set, all of them are required.
/// The challenge key is optional. The disposable domain list is optional as a pair, and once one of its settings is set, both are required.
/// The trial quotas are optional.
/// </summary>
public sealed class SignUpOptions
{
    /// <summary>The built-in applications of the region that a person can start from.</summary>
    public List<SignUpApplicationOptions> Applications { get; } = [];

    /// <summary>
    /// The public mailbox domains, such as <c>gmail.com</c>, whose addresses the sign-up domain limit counts one by one, see
    /// <c>docs/reference/sign-up.md#bots-and-abuse</c>. Empty by default.
    /// </summary>
    public List<string> PublicMailboxDomains { get; } = [];

    /// <summary>The terms of service of the region.</summary>
    public SignUpDocumentOptions Terms { get; set; } = new();

    /// <summary>The data processing agreement of the region.</summary>
    public SignUpDocumentOptions Dpa { get; set; } = new();

    /// <summary>The privacy policy of the region.</summary>
    public SignUpDocumentOptions Privacy { get; set; } = new();

    /// <summary>The address of the region's licences page, which the legal line links.</summary>
    public string? LicencesUrl { get; set; }

    /// <summary>
    /// The base64 HMAC-SHA256 key that signs the proof-of-work challenges of the sign-up page. Optional. It comes from the key-per-file secret
    /// <c>ControlPlane__SignUp__ChallengeKey</c>, never from a plain setting.
    /// </summary>
    public string? ChallengeKey { get; set; }

    /// <summary>
    /// The open-source list of disposable mailbox domains that the weekly <c>refreshDisposableDomains</c> job downloads, and the SHA-256 that pins it, see
    /// <c>docs/reference/sign-up.md#bots-and-abuse</c>. Optional.
    /// </summary>
    public SignUpDisposableDomainsOptions DisposableDomains { get; set; } = new();

    /// <summary>
    /// The quotas that a new trial gets, by quota key, see the settings table in <c>docs/reference/control-plane.md#settings</c>. A key that is absent is
    /// not set, so the cell's default applies. A <c>null</c> value is no limit. Optional. It is internal so the binder skips it:
    /// <see cref="ControlPlaneOptions.Load"/> reads it from the configuration, because the binder would merge <c>null</c> and absent.
    /// </summary>
    internal IReadOnlyDictionary<string, long?> TrialQuotas { get; set; } = new Dictionary<string, long?>(StringComparer.Ordinal);

    internal bool HasDocuments => !string.IsNullOrWhiteSpace(LicencesUrl) || new[] { Terms, Dpa, Privacy }.Any(IsSet);

    private static bool IsSet(SignUpDocumentOptions document) =>
        !string.IsNullOrWhiteSpace(document.Version)
        || !string.IsNullOrWhiteSpace(document.Urls.En)
        || !string.IsNullOrWhiteSpace(document.Urls.Vi);
}

/// <summary>The <c>ControlPlane:SignUp:DisposableDomains</c> settings: the address of the list and the SHA-256 of its content, as hex.</summary>
public sealed class SignUpDisposableDomainsOptions
{
    /// <summary>The address of the list, one domain per line.</summary>
    public string? Url { get; set; }

    /// <summary>The SHA-256 of the list, as 64 hex characters. The job replaces the table only from a list with this hash.</summary>
    public string? Sha256 { get; set; }

    internal bool IsSet => !string.IsNullOrWhiteSpace(Url) || !string.IsNullOrWhiteSpace(Sha256);

    internal bool IsComplete => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Sha256);
}

/// <summary>One entry of <c>ControlPlane:SignUp:Applications</c>: a built-in application, its title and its description in each language.</summary>
public sealed class SignUpApplicationOptions
{
    /// <summary>The application id, such as <c>approvals</c>.</summary>
    public string? Id { get; set; }

    /// <summary>The title that the sign-up page shows, in each language.</summary>
    public SignUpTexts Titles { get; set; } = new();

    /// <summary>The description that the sign-up page shows under the title, in each language.</summary>
    public SignUpTexts Descriptions { get; set; } = new();
}

/// <summary>A text of the sign-up page in its two languages.</summary>
public sealed class SignUpTexts
{
    /// <summary>The English text.</summary>
    public string? En { get; set; }

    /// <summary>The Vietnamese text.</summary>
    public string? Vi { get; set; }
}

/// <summary>One legal document of the region: its version and its address in each language.</summary>
public sealed class SignUpDocumentOptions
{
    /// <summary>The version of the document that the person accepts, such as <c>2026-01</c>.</summary>
    public string? Version { get; set; }

    /// <summary>The address of the document in each language.</summary>
    public SignUpTexts Urls { get; set; } = new();
}
