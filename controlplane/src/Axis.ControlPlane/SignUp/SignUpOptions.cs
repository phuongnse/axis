namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The <c>ControlPlane:SignUp</c> settings: what the sign-up page offers, see the settings table in
/// <c>docs/reference/control-plane.md#settings</c>. The applications are optional. The documents are optional as a group,
/// and once one of their settings is set, all of them are required.
/// </summary>
public sealed class SignUpOptions
{
    /// <summary>The built-in applications of the region that a person can start from.</summary>
    public List<SignUpApplicationOptions> Applications { get; } = [];

    /// <summary>The terms of service of the region.</summary>
    public SignUpDocumentOptions Terms { get; set; } = new();

    /// <summary>The data processing agreement of the region.</summary>
    public SignUpDocumentOptions Dpa { get; set; } = new();

    /// <summary>The privacy policy of the region.</summary>
    public SignUpDocumentOptions Privacy { get; set; } = new();

    /// <summary>The address of the region's licences page, which the legal line links.</summary>
    public string? LicencesUrl { get; set; }

    internal bool HasDocuments => !string.IsNullOrWhiteSpace(LicencesUrl) || new[] { Terms, Dpa, Privacy }.Any(IsSet);

    private static bool IsSet(SignUpDocumentOptions document) =>
        !string.IsNullOrWhiteSpace(document.Version)
        || !string.IsNullOrWhiteSpace(document.Urls.En)
        || !string.IsNullOrWhiteSpace(document.Urls.Vi);
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
