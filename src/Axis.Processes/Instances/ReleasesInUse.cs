using Axis.Configuration.Storage;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;

namespace Axis.Processes.Instances;

/// <summary>
/// The releases a tenant still uses. A release is in use when it is active or when a running or waiting
/// process instance is pinned to it. The start check and the check of a registered package manifest share
/// this query, see docs/reference/extension-packages.md#customer-hosted-runners.
/// </summary>
internal static class ReleasesInUse
{
    /// <summary>The ids of the releases in use in the tenant database, without duplicates, in order of release id.</summary>
    public static async Task<IReadOnlyList<Guid>> ListAsync(
        ConfigurationDbContext configuration,
        ProcessesDbContext processes,
        CancellationToken cancellationToken)
    {
        var active = await configuration.ActiveReleases.AsNoTracking().Select(row => row.ReleaseId).ToListAsync(cancellationToken);
        var open = await processes.Instances
            .AsNoTracking()
            .Where(instance => instance.State == ProcessStarts.Running || instance.State == ProcessStarts.Waiting)
            .Select(instance => instance.ReleaseId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return [.. active.Concat(open).Distinct().Order()];
    }
}
