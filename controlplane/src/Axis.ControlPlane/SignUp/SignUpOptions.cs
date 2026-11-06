namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The <c>ControlPlane:SignUp</c> settings: what the sign-up page offers, see the settings table in
/// <c>docs/reference/control-plane.md#settings</c>. All of them are optional.
/// </summary>
public sealed class SignUpOptions
{
    /// <summary>The built-in applications of the region that a person can start from.</summary>
    public List<SignUpApplicationOptions> Applications { get; } = [];
}

/// <summary>One entry of <c>ControlPlane:SignUp:Applications</c>: a built-in application and its title in each language.</summary>
public sealed class SignUpApplicationOptions
{
    /// <summary>The application id, such as <c>approvals</c>.</summary>
    public string? Id { get; set; }

    /// <summary>The title that the sign-up page shows, in each language.</summary>
    public SignUpApplicationTitles Titles { get; set; } = new();
}

/// <summary>The title of a sign-up application in the two languages of the page.</summary>
public sealed class SignUpApplicationTitles
{
    /// <summary>The English title.</summary>
    public string? En { get; set; }

    /// <summary>The Vietnamese title.</summary>
    public string? Vi { get; set; }
}
