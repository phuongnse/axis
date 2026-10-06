namespace Axis.Configuration.Storage;

/// <summary>
/// The release that is active for an application. There is at most one row per application id,
/// and the manifest name is unique across applications, ignoring letter case.
/// </summary>
public sealed class ActiveReleaseRow
{
    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The manifest name as written in the active release.</summary>
    public required string Name { get; init; }

    public required Guid ReleaseId { get; init; }

    public required DateTimeOffset ActivatedAt { get; init; }
}
