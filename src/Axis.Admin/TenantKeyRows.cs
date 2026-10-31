using System.Security.Cryptography;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Admin;

/// <summary>Makes the first data key and the first lookup key of a tenant: 32 random bytes each, stored encrypted by the installation key.</summary>
internal static class TenantKeyRows
{
    private const int KeyBytes = 32;

    /// <summary>
    /// Adds number 1 of each kind that the tenant has no key of, and leaves the keys it has. It does not save, so the caller
    /// saves the rows with its own change. The tenant row must be added or saved before the save.
    /// </summary>
    public static async Task AddMissingAsync(PlatformDbContext platform, string tenantId, TenantSecretCipher cipher, CancellationToken cancellationToken)
    {
        var existing = await platform.TenantDataKeys
            .Where(key => key.TenantId == tenantId)
            .Select(key => key.Kind)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!existing.Contains(TenantKeyKinds.Data))
        {
            Add(platform, tenantId, cipher, TenantKeyKinds.Data, 1);
        }

        if (!existing.Contains(TenantKeyKinds.Lookup))
        {
            Add(platform, tenantId, cipher, TenantKeyKinds.Lookup, 1);
        }
    }

    /// <summary>Adds a key of the kind with the number, 32 random bytes encrypted by the installation key. It does not save.</summary>
    public static void Add(PlatformDbContext platform, string tenantId, TenantSecretCipher cipher, string kind, int number) =>
        platform.TenantDataKeys.Add(new TenantDataKeyRow
        {
            TenantId = tenantId,
            Kind = kind,
            Number = number,
            Key = cipher.Encrypt(
                tenantId,
                kind == TenantKeyKinds.Data ? TenantSecretFields.DataKey(number) : TenantSecretFields.LookupKey(number),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes))),
        });
}
