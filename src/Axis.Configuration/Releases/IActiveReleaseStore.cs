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
    /// Makes <paramref name="releaseId"/> the active release of <paramref name="applicationId"/>
    /// under <paramref name="name"/>, replacing any previous one; the last call wins. Returns
    /// <see langword="false"/> and changes nothing when another application id holds the name,
    /// ignoring letter case.
    /// </summary>
    Task<bool> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default);
}
