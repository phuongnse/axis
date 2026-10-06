namespace Axis.Configuration.Storage;

/// <summary>
/// A site path of an active release. A path is held by at most one application; the rows of an
/// application are removed together with its active release.
/// </summary>
public sealed class ActiveSiteRow
{
    /// <summary>The <c>id</c> of the application manifest whose active release has the site.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The site path as written in the active release, which is always lower-case.</summary>
    public required string Path { get; init; }
}
