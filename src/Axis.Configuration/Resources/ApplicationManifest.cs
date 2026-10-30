namespace Axis.Configuration.Resources;

/// <summary>The <c>application</c> resource at the root of an application folder.</summary>
public sealed record ApplicationManifest : Resource
{
    /// <summary>
    /// Proposed: one line that says what the application as a whole is for, which the platform home page shows under
    /// its title. Each site's own description says what that site does.
    /// </summary>
    public TextReference? Description { get; init; }

    /// <summary>
    /// The application's IANA time zone id, such as <c>Asia/Ho_Chi_Minh</c>, or null for UTC. It decides the date that
    /// <c>today()</c> and <c>toDate</c> give and the year of a sequence number.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>The deployment binding that names where the files of file fields are stored, or null for the tenant database.</summary>
    public BindingReference? FileStorage { get; init; }

    /// <summary>The extension packages the application uses, in the order written.</summary>
    public IReadOnlyList<ApplicationPackageDefinition> Packages { get; init; } = [];

    /// <summary>The modules the application uses, in the order written.</summary>
    public IReadOnlyList<ApplicationModuleDefinition> Modules { get; init; } = [];
}
