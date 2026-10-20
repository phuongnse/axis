namespace Axis.Server.Studio;

/// <summary>
/// The server settings of the authoring API. <c>Studio:Folders</c> lists the application folders
/// Studio may edit, resolved against the content root. <c>Studio:AuthorRole</c> is the roles claim
/// value an author holds. <c>Studio:ApplicationsPath</c> is the folder where Studio creates applications, created at
/// start when missing. With no folder, no applications path or no role, Studio is off and nobody may use it.
/// <c>Studio:PublisherRole</c> is the roles claim value that may also publish. With no value, nobody may.
/// <c>Studio:Tests</c> shows the test controls to the users of Studio. It is off by default and grants no right.
/// </summary>
internal sealed record StudioOptions(IReadOnlyList<string> Folders, string? AuthorRole, string? PublisherRole, bool Tests = false, string? ApplicationsPath = null)
{
    public const string SectionName = "Studio";

    public static StudioOptions Load(IConfiguration configuration, string contentRoot)
    {
        var section = configuration.GetSection(SectionName);
        var folders = section.GetSection("Folders").Get<string[]>() ?? [];
        var role = section["AuthorRole"];
        var publisherRole = section["PublisherRole"];
        var applicationsPath = section["ApplicationsPath"];
        string? applications = null;
        if (!string.IsNullOrWhiteSpace(applicationsPath))
        {
            applications = Path.GetFullPath(applicationsPath, contentRoot);
            try
            {
                Directory.CreateDirectory(applications);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"The folder '{applications}' set by 'Studio:ApplicationsPath' cannot be created.", exception);
            }
        }

        return new StudioOptions(
            [.. folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => Path.GetFullPath(folder, contentRoot))],
            string.IsNullOrWhiteSpace(role) ? null : role,
            string.IsNullOrWhiteSpace(publisherRole) ? null : publisherRole,
            section.GetValue<bool>("Tests"),
            applications);
    }

    /// <summary>True when Studio is on and <paramref name="roles"/> holds the author role.</summary>
    public bool Allows(IReadOnlyList<string> roles) =>
        (Folders.Count > 0 || ApplicationsPath is not null) && AuthorRole is not null && roles.Contains(AuthorRole, StringComparer.Ordinal);

    /// <summary>True when <see cref="Allows"/> holds and <paramref name="roles"/> holds the publisher role.</summary>
    public bool AllowsPublish(IReadOnlyList<string> roles) =>
        Allows(roles) && PublisherRole is not null && roles.Contains(PublisherRole, StringComparer.Ordinal);

    /// <summary>True when <see cref="Allows"/> holds and <c>Studio:ApplicationsPath</c> is set.</summary>
    public bool AllowsCreate(IReadOnlyList<string> roles) => Allows(roles) && ApplicationsPath is not null;

    /// <summary>True when <see cref="Allows"/> holds and <c>Studio:Tests</c> is on.</summary>
    public bool AllowsTests(IReadOnlyList<string> roles) => Allows(roles) && Tests;
}
