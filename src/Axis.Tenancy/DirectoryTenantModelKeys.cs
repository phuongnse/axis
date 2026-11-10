using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy;

/// <summary>
/// The own model keys of tenants in the <c>directory</c> source: <c>model_key</c> of <c>platform.tenants</c>, decrypted with the installation
/// key, see <c>docs/reference/model-bindings.md#a-tenants-own-key</c>. Each read opens its own connection, so a key that an administrator
/// sets, changes or removes is seen at the next read.
/// </summary>
public sealed class DirectoryTenantModelKeys(string platformConnectionString, TenantSecretCipher cipher)
{
    /// <summary>The tenant's own key, or <see langword="null"/> when the tenant has none.</summary>
    /// <exception cref="InvalidOperationException">The stored key does not decrypt. The message names the tenant and the field and never the value.</exception>
    public async Task<SecretValue?> ReadAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
        var stored = await platform.Tenants.AsNoTracking().Where(row => row.Id == tenantId).Select(row => row.ModelKey).SingleOrDefaultAsync(cancellationToken);
        return stored is { Length: > 0 } ? cipher.Decrypt(tenantId, TenantSecretFields.ModelKey, stored) : null;
    }
}
