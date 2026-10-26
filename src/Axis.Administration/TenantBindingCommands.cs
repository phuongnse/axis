using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>One binding as <c>tenant binding list</c> shows it. A secret's value is never read, only whether it is set.</summary>
public sealed record TenantBindingView(string Name, string Kind, string? Url, bool HasSecret);

/// <summary>
/// The rules of <c>tenant binding list</c>, <c>set</c> and <c>remove</c>. Every rule is checked before the first write, and a
/// change and its platform audit record share one transaction. A message never holds a value.
/// </summary>
public static class TenantBindingCommands
{
    private const string Conflict = "Another command changed the binding at the same moment. Run the command again.";

    /// <summary>Returns the bindings of the tenant ordered by name, or <see langword="null"/> when the tenant does not exist.</summary>
    public static async Task<IReadOnlyList<TenantBindingView>?> ListAsync(PlatformDbContext platform, string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!await platform.Tenants.AsNoTracking().AnyAsync(tenant => tenant.Id == tenantId, cancellationToken))
        {
            return null;
        }

        // The encrypted value is read only to tell whether one is set, and is never decrypted.
        var rows = await platform.TenantBindings.AsNoTracking()
            .Where(binding => binding.TenantId == tenantId)
            .Select(binding => new { binding.Name, binding.Kind, binding.Url, HasSecret = binding.SecretValue != null && binding.SecretValue.Length > 0 })
            .ToListAsync(cancellationToken);
        return [.. rows
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .Select(row => new TenantBindingView(row.Name, row.Kind, row.Kind == DeploymentBindingKinds.Url ? row.Url : null, row.HasSecret))];
    }

    /// <summary>Adds or replaces a <c>url</c> binding. The value is an absolute http or https URL.</summary>
    public static async Task<TenantSettingResult> SetUrlAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        string name,
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (string.IsNullOrWhiteSpace(name))
        {
            return TenantSettingResult.Refused("A binding name must not be blank.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return TenantSettingResult.Refused($"The binding '{name}' must be an absolute http or https URL.");
        }

        return await SetAsync(platform, command, tenantId, name, DeploymentBindingKinds.Url, url, null, cancellationToken);
    }

    /// <summary>Adds or replaces a <c>secret</c> binding. The value is stored encrypted, bound to the tenant and the binding.</summary>
    public static async Task<TenantSettingResult> SetSecretAsync(
        PlatformDbContext platform,
        TenantSecretCipher cipher,
        string command,
        string tenantId,
        string name,
        SecretValue value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(name))
        {
            return TenantSettingResult.Refused("A binding name must not be blank.");
        }

        var plain = value.Reveal();
        if (plain.Length == 0)
        {
            return TenantSettingResult.Refused($"The value of the binding '{name}' must not be empty.");
        }

        return await SetAsync(platform, command, tenantId, name, DeploymentBindingKinds.Secret, null, cipher.Encrypt(tenantId, TenantSecretFields.Binding(name), plain), cancellationToken);
    }

    /// <summary>Removes a binding. A binding that does not exist is refused.</summary>
    public static async Task<TenantSettingResult> RemoveAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        var existing = await FindAsync(platform, tenantId, name, cancellationToken);
        if (existing is null)
        {
            return TenantSettingResult.Refused($"The tenant '{tenantId}' has no binding '{name}'.");
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            [$"binding:{existing.Name}"],
            [tenantId],
            _ =>
            {
                platform.TenantBindings.Remove(existing);
                return Task.FromResult<TenantSettingResult?>(null);
            },
            Conflict,
            cancellationToken);
    }

    private static async Task<TenantSettingResult> SetAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        string name,
        string kind,
        string? url,
        byte[]? secretValue,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        var existing = await FindAsync(platform, tenantId, name, cancellationToken);
        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            [$"binding:{existing?.Name ?? name}"],
            [tenantId],
            _ =>
            {
                // A replaced binding keeps its stored name: the name is part of the key, and names ignore letter case.
                if (existing is null)
                {
                    platform.TenantBindings.Add(new TenantBindingRow { TenantId = tenantId, Name = name, Kind = kind, Url = url, SecretValue = secretValue });
                }
                else
                {
                    existing.Kind = kind;
                    existing.Url = url;
                    existing.SecretValue = secretValue;
                }

                return Task.FromResult<TenantSettingResult?>(null);
            },
            Conflict,
            cancellationToken);
    }

    private static Task<TenantBindingRow?> FindAsync(PlatformDbContext platform, string tenantId, string name, CancellationToken cancellationToken)
    {
        string[] keys = [name.ToLowerInvariant()];
        return platform.TenantBindings.FirstOrDefaultAsync(binding => binding.TenantId == tenantId && keys.Contains(binding.Name.ToLower()), cancellationToken);
    }
}
