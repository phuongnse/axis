using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy.Keys;

/// <summary>
/// The keys of the <c>directory</c> source: the rows of <c>platform.tenant_data_keys</c>, decrypted with the installation key.
/// Each read opens its own connection, so a key added by <c>tenant create</c> or <c>migrate</c> is seen at the next read.
/// </summary>
public sealed class DirectoryTenantKeySource(string platformConnectionString, TenantSecretCipher cipher) : ITenantKeySource
{
    public async Task<IReadOnlyList<TenantKey>> ReadAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
        var rows = await platform.TenantDataKeys.AsNoTracking().Where(row => row.TenantId == tenantId).ToListAsync(cancellationToken);
        return [.. rows.Select(row => Decode(tenantId, row))];
    }

    private TenantKey Decode(string tenantId, TenantDataKeyRow row)
    {
        var (kind, field) = row.Kind switch
        {
            TenantKeyKinds.Data => (TenantKeyKind.Data, TenantSecretFields.DataKey(row.Number)),
            TenantKeyKinds.Lookup => (TenantKeyKind.Lookup, TenantSecretFields.LookupKey(row.Number)),
            _ => throw new InvalidOperationException($"The tenant '{tenantId}' has a key of unknown kind '{row.Kind}'."),
        };

        var text = cipher.Decrypt(tenantId, field, row.Key).Reveal();
        var buffer = new byte[TenantKey.Size + 1];
        if (!Convert.TryFromBase64String(text, buffer, out var length) || length != TenantKey.Size)
        {
            throw new InvalidOperationException($"The {field} of tenant '{tenantId}' is not {TenantKey.Size} bytes in base64.");
        }

        return new TenantKey(kind, row.Number, buffer[..TenantKey.Size]);
    }
}
