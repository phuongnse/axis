using Axis.Tenancy;

namespace Axis.Server.Studio;

/// <summary>
/// The server settings of the authoring API. <c>Studio:Folders</c> lists the application folders
/// Studio may edit, resolved against the content root. <c>Studio:AuthorRole</c> is the roles claim
/// value an author holds. <c>Studio:ApplicationsPath</c> is the folder where Studio creates applications, created at
/// start when missing. With no folder, no applications path or no role, Studio is off and nobody may use it.
/// <c>Studio:PublisherRole</c> is the roles claim value that may also publish. With no value, nobody may.
/// <c>Studio:Tests</c> shows the test controls to the users of Studio. It is off by default and grants no right.
/// <c>Studio:RevisionsPath</c> is the folder that holds the git repository of each application folder, outside the folder.
/// It defaults to <c>.revisions</c> under <c>Studio:ApplicationsPath</c>, and with neither path no revisions are kept.
/// In a cell, <c>Studio:Folders</c> and <c>Studio:RevisionsPath</c> must be empty and <see cref="PerTenant"/> is on, so a
/// request reaches only <c>Studio:ApplicationsPath/&lt;tenant id&gt;</c> and its <c>.revisions</c>, see <see cref="ForTenant"/>.
/// </summary>
internal sealed record StudioOptions(
    IReadOnlyList<string> Folders,
    string? AuthorRole,
    string? PublisherRole,
    bool Tests = false,
    string? ApplicationsPath = null,
    bool PerTenant = false,
    string? RevisionsPath = null)
{
    public const string SectionName = "Studio";

    public static StudioOptions Load(IConfiguration configuration, string contentRoot, CellOptions cell)
    {
        var section = configuration.GetSection(SectionName);
        var folders = section.GetSection("Folders").Get<string[]>() ?? [];
        if (cell.IsCell && folders.Any(folder => !string.IsNullOrWhiteSpace(folder)))
        {
            throw new InvalidOperationException(
                "Studio:Folders must be empty when Cell:Id is set, because every tenant would reach its folders.");
        }

        var revisionsPath = section["RevisionsPath"];
        if (cell.IsCell && !string.IsNullOrWhiteSpace(revisionsPath))
        {
            throw new InvalidOperationException(
                "Studio:RevisionsPath must be empty when Cell:Id is set, because each tenant's revisions live in its own folder.");
        }

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

        string? revisions = null;
        if (!string.IsNullOrWhiteSpace(revisionsPath))
        {
            revisions = Path.GetFullPath(revisionsPath, contentRoot);
        }
        else if (applications is not null)
        {
            revisions = Path.Combine(applications, ".revisions");
        }

        return new StudioOptions(
            [.. folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => Path.GetFullPath(folder, contentRoot))],
            string.IsNullOrWhiteSpace(role) ? null : role,
            string.IsNullOrWhiteSpace(publisherRole) ? null : publisherRole,
            section.GetValue<bool>("Tests"),
            applications,
            cell.IsCell,
            revisions);
    }

    /// <summary>
    /// The options of one request. Outside a cell they are these options. In a cell they name only the folder
    /// <c>Studio:ApplicationsPath/&lt;tenantId&gt;</c>, created when missing, and its revisions in the <c>.revisions</c> folder inside it. With no applications path, no tenant, or a
    /// tenant id that is not a plain folder name, they name no applications folder, so Studio answers as if it were off.
    /// </summary>
    public StudioOptions ForTenant(string? tenantId)
    {
        if (!PerTenant)
        {
            return this;
        }

        if (ApplicationsPath is not { } root
            || string.IsNullOrWhiteSpace(tenantId)
            || tenantId is "." or ".."
            || Path.GetFileName(tenantId) != tenantId)
        {
            return this with { ApplicationsPath = null, PerTenant = false, RevisionsPath = null };
        }

        var folder = Path.Combine(Path.TrimEndingDirectorySeparator(root), tenantId);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"The folder '{folder}' of the tenant '{tenantId}' cannot be created.", exception);
        }

        return this with { ApplicationsPath = folder, PerTenant = false, RevisionsPath = Path.Combine(folder, ".revisions") };
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
