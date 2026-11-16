using Axis.Configuration.Releases;
using Axis.Configuration.Storage;

namespace Axis.Studio;

/// <summary>
/// Counts a tenant's applications the way its <c>applications</c> quota does, see <c>docs/reference/tenant-quotas.md#usage</c>. The
/// server's create check, the usage route and the worker's sampling all use it, so they always agree.
/// </summary>
internal static class StudioApplicationCount
{
    /// <summary>
    /// The number of distinct names, ignoring letter case, of the applications in the Studio folders of <paramref name="options"/> and of the
    /// tenant's active releases.
    /// </summary>
    public static async Task<int> CountAsync(StudioOptions options, ConfigurationDbContext configuration, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StudioFolder.Applications(options), StringComparer.OrdinalIgnoreCase);
        var releases = await new ActiveReleaseStore(configuration).ListAsync(cancellationToken);
        names.UnionWith(releases.Select(release => release.Name));
        return names.Count;
    }
}
