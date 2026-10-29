using Axis.Configuration.Resources;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>
/// The rules of <c>tenant host add</c>, <c>remove</c> and <c>move</c>. A host is unique ignoring letter case across all tenants.
/// Every rule is checked before the first write, and a change and its platform audit record share one transaction.
/// A host may belong to one site of its tenant, and a site has at most one host per tenant.
/// </summary>
public static class TenantHostCommands
{
    private const string Conflict = "A host belongs to another tenant.";

    /// <summary>
    /// Adds a host to a tenant. A host that any tenant holds is refused. With <paramref name="sitePath"/> the host belongs to that site,
    /// stored in lower case. A path with bad syntax, a reserved path, or a site that the tenant already has a host for is refused.
    /// The command does not check that a release has the site.
    /// </summary>
    public static async Task<TenantSettingResult> AddAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        string host,
        string? sitePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (string.IsNullOrWhiteSpace(host))
        {
            return TenantSettingResult.Refused("A host must not be blank.");
        }

        string? path = null;
        if (sitePath is not null)
        {
            path = sitePath.Trim().ToLowerInvariant();
            if (!SitePaths.IsValid(path))
            {
                return TenantSettingResult.Refused($"The site path '{sitePath.Trim()}' is not a valid path.");
            }

            if (SitePaths.Reserved.Contains(path))
            {
                return TenantSettingResult.Refused($"The site path '{path}' is reserved.");
            }
        }

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        host = host.Trim();
        string[] keys = [host.ToLowerInvariant()];
        if (await platform.TenantHosts.AnyAsync(row => keys.Contains(row.Host.ToLower()), cancellationToken))
        {
            return TenantSettingResult.Refused($"The host '{host}' belongs to a tenant.");
        }

        if (path is not null && await platform.TenantHosts.AnyAsync(row => row.TenantId == tenantId && row.SitePath == path, cancellationToken))
        {
            return TenantSettingResult.Refused($"The tenant '{tenantId}' already has a host for the site '{path}'.");
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            path is null ? [$"host:{host}"] : [$"host:{host}", $"site:{path}"],
            [tenantId],
            _ =>
            {
                platform.TenantHosts.Add(new TenantHostRow { TenantId = tenantId, Host = host, SitePath = path });
                return Task.FromResult<TenantSettingResult?>(null);
            },
            Conflict,
            cancellationToken);
    }

    /// <summary>
    /// Removes a host from a tenant. Removing the last host of the tenant is refused, and so is removing the last host
    /// that belongs to no site.
    /// </summary>
    public static async Task<TenantSettingResult> RemoveAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        string host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (string.IsNullOrWhiteSpace(host))
        {
            return TenantSettingResult.Refused("A host must not be blank.");
        }

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        var key = host.Trim().ToLowerInvariant();
        var held = await platform.TenantHosts.Where(row => row.TenantId == tenantId).ToListAsync(cancellationToken);
        var existing = held.FirstOrDefault(row => string.Equals(row.Host, key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return TenantSettingResult.Refused($"The tenant '{tenantId}' does not hold the host '{host.Trim()}'.");
        }

        if (held.Count == 1)
        {
            return TenantSettingResult.Refused($"The host '{existing.Host}' is the last host of the tenant '{tenantId}'.");
        }

        if (existing.SitePath is null && held.All(row => row == existing || row.SitePath is not null))
        {
            return TenantSettingResult.Refused($"The host '{existing.Host}' is the last host of the tenant '{tenantId}' that is no site's.");
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            [$"host:{existing.Host}"],
            [tenantId],
            _ =>
            {
                platform.TenantHosts.Remove(existing);
                return Task.FromResult<TenantSettingResult?>(null);
            },
            Conflict,
            cancellationToken);
    }

    /// <summary>
    /// Moves a host to another tenant in one transaction. It writes one record, on the target tenant, that names the host and the
    /// tenant it came from. The host keeps its site, so a target tenant that already has a host for that site is refused.
    /// The source tenant may be left with no hosts.
    /// </summary>
    public static async Task<TenantSettingResult> MoveAsync(
        PlatformDbContext platform,
        string command,
        string host,
        string toTenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(toTenantId);

        if (string.IsNullOrWhiteSpace(host))
        {
            return TenantSettingResult.Refused("A host must not be blank.");
        }

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, toTenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        host = host.Trim();
        string[] keys = [host.ToLowerInvariant()];
        var existing = await platform.TenantHosts.AsNoTracking().FirstOrDefaultAsync(row => keys.Contains(row.Host.ToLower()), cancellationToken);
        if (existing is null)
        {
            return TenantSettingResult.Refused($"No tenant holds the host '{host}'.");
        }

        if (existing.TenantId == toTenantId)
        {
            return TenantSettingResult.Refused($"The tenant '{toTenantId}' already holds the host '{existing.Host}'.");
        }

        if (existing.SitePath is not null
            && await platform.TenantHosts.AnyAsync(row => row.TenantId == toTenantId && row.SitePath == existing.SitePath, cancellationToken))
        {
            return TenantSettingResult.Refused($"The tenant '{toTenantId}' already has a host for the site '{existing.SitePath}'.");
        }

        var fromTenantId = existing.TenantId;
        problem = await TenantSettingWrites.ChangeProblemAsync(platform, fromTenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            toTenantId,
            [$"host:{existing.Host}", $"from:{fromTenantId}"],
            [toTenantId, fromTenantId],
            async token =>
            {
                // EF cannot change a key property, so the row moves with one statement.
                var moved = await platform.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE platform.tenant_hosts SET tenant_id = {toTenantId} WHERE lower(host) = {keys[0]} AND tenant_id = {fromTenantId}",
                    token);
                return moved == 1 ? null : TenantSettingResult.Refused($"The host '{existing.Host}' was changed by another command. Run the command again.");
            },
            Conflict,
            cancellationToken);
    }
}
