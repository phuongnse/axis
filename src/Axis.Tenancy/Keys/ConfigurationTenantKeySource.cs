using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Keys;

/// <summary>
/// The keys of the <c>configuration</c> source: <c>Tenants:{id}:DataKeys:{number}</c> and <c>Tenants:{id}:LookupKeys:{number}</c>,
/// each 32 bytes in base64. Each read looks at the configuration again, so a key added to a reloadable setting is seen at the
/// next read.
/// </summary>
public sealed class ConfigurationTenantKeySource(IConfiguration configuration) : ITenantKeySource
{
    public Task<IReadOnlyList<TenantKey>> ReadAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var tenant = configuration.GetSection(TenantOptions.SectionName).GetSection(tenantId);
        var keys = new List<TenantKey>();
        Read(tenant.GetSection("DataKeys"), TenantKeyKind.Data, keys);
        Read(tenant.GetSection("LookupKeys"), TenantKeyKind.Lookup, keys);
        return Task.FromResult<IReadOnlyList<TenantKey>>(keys);
    }

    private static void Read(IConfigurationSection section, TenantKeyKind kind, List<TenantKey> keys)
    {
        foreach (var child in section.GetChildren())
        {
            var setting = $"{section.Path}:{child.Key}";
            if (!int.TryParse(child.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            {
                throw new InvalidOperationException($"The key number of '{setting}' must be an integer of at least 1.");
            }

            var buffer = new byte[TenantKey.Size + 1];
            if (child.Value is not { } value || !Convert.TryFromBase64String(value.Trim(), buffer, out var length) || length != TenantKey.Size)
            {
                throw new InvalidOperationException($"The setting '{setting}' must be {TenantKey.Size} bytes in base64.");
            }

            keys.Add(new TenantKey(kind, number, buffer[..TenantKey.Size]));
        }
    }
}
