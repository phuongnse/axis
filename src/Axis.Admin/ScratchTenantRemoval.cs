using System.Text.RegularExpressions;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// Removes a restore-check scratch tenant at once, with no grace window: its directory row, which cascades to its hosts, bindings, keys,
/// backup key, backup time, quotas, usage and leases, then its database, its schema and runtime roles, its realm in the identity provider and
/// its folder under <c>Studio:ApplicationsPath</c>. Every part is found from the id alone, never from the directory row, so a part already gone
/// counts as done and a rerun after a stop finishes the job. Platform audit records that name the tenant stay. It refuses any id that does not
/// start with <see cref="IdPrefix"/> before it touches anything, so a real tenant is never removed this way. The admin role is not a superuser,
/// so it grants itself the tenant's roles before it drops the database. An error never carries a host or a connection string.
/// </summary>
internal sealed partial class ScratchTenantRemoval(
    NpgsqlConnectionStringBuilder admin,
    string platformConnectionString,
    TenantRealm realm,
    string? studioApplicationsPath)
{
    public const string IdPrefix = "restore-check-";

    /// <summary>Whether the id starts with <see cref="IdPrefix"/> and is a tenant id: 1 to 50 lowercase letters, digits and hyphens.</summary>
    public static bool IsScratchId(string id) =>
        id.StartsWith(IdPrefix, StringComparison.Ordinal) && IdPattern().IsMatch(id);

    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsScratchId(id))
        {
            throw new InvalidOperationException($"only a scratch tenant, whose id starts with {IdPrefix}, can be removed");
        }

        // The row goes first: no host resolves to the tenant and its keys are destroyed at once. The other parts are found again from the id.
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options))
        {
            await platform.Tenants.Where(tenant => tenant.Id == id).ExecuteDeleteAsync(cancellationToken);
        }

        await TenantPartRemoval.RemoveDatabaseAndRolesAsync(admin, id, cancellationToken);
        await realm.DeleteAsync(id, cancellationToken);
        TenantPartRemoval.RemoveStudioFolder(studioApplicationsPath, id);
    }

    [GeneratedRegex(@"^[a-z0-9-]{1,50}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
