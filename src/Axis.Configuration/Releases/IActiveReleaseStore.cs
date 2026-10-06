using Axis.Configuration.Storage;

namespace Axis.Configuration.Releases;

/// <summary>
/// The active release of each application in the tenant database. Other modules read and set the
/// active release only through this contract.
/// </summary>
public interface IActiveReleaseStore
{
    /// <summary>Finds the active release whose manifest name matches <paramref name="name"/>, ignoring letter case.</summary>
    Task<ActiveRelease?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<ActiveRelease?> FindByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the release <paramref name="releaseId"/> with its resources in ordinal path order, or
    /// returns <see langword="null"/> when there is no such release.
    /// </summary>
    Task<Release?> GetReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default);

    /// <summary>Lists every active release, ordered by manifest name ignoring letter case.</summary>
    Task<IReadOnlyList<ActiveRelease>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the active release that holds the site path <paramref name="path"/>. The path is
    /// compared exactly; site paths are always lower-case.
    /// </summary>
    Task<ActiveRelease?> FindBySitePathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes <paramref name="releaseId"/> the active release of <paramref name="applicationId"/>
    /// under <paramref name="name"/> with the site paths <paramref name="sitePaths"/>, replacing any
    /// previous one; the last call wins. The release and its site paths are written in one
    /// transaction, so call it outside an explicit transaction. Returns a conflict and changes
    /// nothing when another application id holds the name, ignoring letter case, or one of the
    /// site paths.
    /// </summary>
    Task<SetActiveReleaseResult> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default);
}
