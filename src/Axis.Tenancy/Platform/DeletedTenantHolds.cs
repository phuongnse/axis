using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy.Platform;

/// <summary>
/// What a purged tenant leaves held back, read from its tombstone in <c>platform.deleted_tenants</c>. A deleted tenant's id is never used again,
/// and its hosts are refused for <see cref="HostHoldBackDays"/> days, so a new tenant does not receive the old tenant's links and emails.
/// <c>tenant create</c>, <c>tenant host add</c> and <c>move</c> and the provisioning API's create and host routes all use these checks.
/// </summary>
public static class DeletedTenantHolds
{
    /// <summary>How long a deleted tenant's hosts are refused, counted from the tombstone's <c>deleted_at</c>.</summary>
    public const int HostHoldBackDays = 90;

    /// <summary>Whether a tombstone has this tenant id.</summary>
    public static Task<bool> IsDeletedIdAsync(PlatformDbContext platform, string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return platform.DeletedTenants.AnyAsync(row => row.Id == id, cancellationToken);
    }

    /// <summary>
    /// Whether a tombstone less than <see cref="HostHoldBackDays"/> days old has any of these hosts, ignoring letter case.
    /// The age is counted on the database clock, the clock the purge writes <c>deleted_at</c> with.
    /// </summary>
    public static async Task<bool> IsHostHeldBackAsync(PlatformDbContext platform, IReadOnlyCollection<string> hosts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(hosts);

        var keys = hosts.Select(host => host.Trim().ToLowerInvariant()).ToArray();
        if (keys.Length == 0)
        {
            return false;
        }

        return await platform.Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM platform.deleted_tenants AS d, unnest(d.hosts) AS h(host)
                    WHERE d.deleted_at > now() - make_interval(days => {HostHoldBackDays}) AND lower(h.host) = ANY({keys})) AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }
}
