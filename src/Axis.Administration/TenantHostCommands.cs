using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>
/// The rules of <c>tenant host add</c>, <c>remove</c> and <c>move</c>. A host is unique ignoring letter case across all tenants.
/// Every rule is checked before the first write, and a change and its platform audit record share one transaction.
/// </summary>
public static class TenantHostCommands
{
    private const string Conflict = "A host belongs to another tenant.";

    /// <summary>Adds a host to a tenant. A host that any tenant holds is refused.</summary>
    public static async Task<TenantSettingResult> AddAsync(
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

        host = host.Trim();
        string[] keys = [host.ToLowerInvariant()];
        if (await platform.TenantHosts.AnyAsync(row => keys.Contains(row.Host.ToLower()), cancellationToken))
        {
            return TenantSettingResult.Refused($"The host '{host}' belongs to a tenant.");
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            [$"host:{host}"],
            [tenantId],
            _ =>
            {
                platform.TenantHosts.Add(new TenantHostRow { TenantId = tenantId, Host = host });
                return Task.FromResult<TenantSettingResult?>(null);
            },
            Conflict,
            cancellationToken);
    }

    /// <summary>Removes a host from a tenant. Removing the last host of the tenant is refused.</summary>
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
    /// tenant it came from. The source tenant may be left with no hosts.
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
