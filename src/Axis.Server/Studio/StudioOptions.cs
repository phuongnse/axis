namespace Axis.Server.Studio;

/// <summary>
/// The server settings of the authoring API. <c>Studio:Folders</c> lists the application folders
/// Studio may edit, resolved against the content root. <c>Studio:AuthorRole</c> is the roles claim
/// value an author holds. With no folder or no role, Studio is off and nobody may use it.
/// </summary>
internal sealed record StudioOptions(IReadOnlyList<string> Folders, string? AuthorRole)
{
    public const string SectionName = "Studio";

    public static StudioOptions Load(IConfiguration configuration, string contentRoot)
    {
        var section = configuration.GetSection(SectionName);
        var folders = section.GetSection("Folders").Get<string[]>() ?? [];
        var role = section["AuthorRole"];
        return new StudioOptions(
            [.. folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => Path.GetFullPath(folder, contentRoot))],
            string.IsNullOrWhiteSpace(role) ? null : role);
    }

    /// <summary>True when Studio is on and <paramref name="roles"/> holds the author role.</summary>
    public bool Allows(IReadOnlyList<string> roles) =>
        Folders.Count > 0 && AuthorRole is not null && roles.Contains(AuthorRole, StringComparer.Ordinal);
}
