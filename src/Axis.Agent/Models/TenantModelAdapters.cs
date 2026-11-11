using Axis.Tenancy;

namespace Axis.Agent.Models;

/// <summary>
/// Hands out the adapter of a binding for a tenant. With <c>Ai:AllowTenantKeys</c> on and a key stored for the tenant, the adapter sends the
/// tenant's key in place of the installation's key. In every other case, such as the setting off, no key stored or
/// <c>Tenancy:Source=configuration</c>, it sends the installation's key. The key is read and decrypted on each call and never logged, see
/// <c>docs/reference/model-bindings.md#a-tenants-own-key</c>. A stored key that does not decrypt fails the call and never falls back to the
/// installation's key.
/// </summary>
public sealed class TenantModelAdapters(ModelAdapters adapters, TenantModelKeySettings settings, DirectoryTenantModelKeys? keys = null)
{
    /// <summary>The adapter of the binding for the tenant.</summary>
    /// <exception cref="InvalidOperationException">
    /// No binding has this name, or the tenant's stored key does not decrypt. The message names the setting or the tenant and never a key.
    /// </exception>
    public async Task<IModelAdapter> ForAsync(string tenantId, string bindingName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(bindingName);

        if (!settings.Allowed || keys is null)
        {
            return adapters.For(bindingName);
        }

        var key = await keys.ReadAsync(tenantId, cancellationToken);
        return key is null ? adapters.For(bindingName) : adapters.WithKey(bindingName, key.Reveal());
    }
}
