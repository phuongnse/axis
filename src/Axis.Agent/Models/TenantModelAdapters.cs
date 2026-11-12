using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Agent.Models;

/// <summary>
/// Hands out the adapter of a binding for a tenant. With <c>Ai:AllowTenantKeys</c> on and a key stored for the tenant, the adapter sends the
/// tenant's key in place of the installation's key. In every other case, such as the setting off, no key stored or
/// <c>Tenancy:Source=configuration</c>, it sends the installation's key. The key is read and decrypted on each call and never logged, see
/// <c>docs/reference/model-bindings.md#a-tenants-own-key</c>. A stored key that does not decrypt fails the call and never falls back to the
/// installation's key. With a <see cref="TenantUsageStore"/>, which is there under <c>Tenancy:Source=directory</c>, every adapter it hands out adds the tokens of each
/// call to the tenant's usage by binding and model, with the tenant's own key counted apart, see <c>docs/reference/tenant-quotas.md#usage</c>.
/// </summary>
public sealed class TenantModelAdapters(
    ModelAdapters adapters,
    AiSettings ai,
    TenantModelKeySettings settings,
    ILogger<TenantModelAdapters> logger,
    TimeProvider time,
    DirectoryTenantModelKeys? keys = null,
    TenantUsageStore? usage = null)
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
            return Recording(adapters.For(bindingName), tenantId, bindingName, ownKey: false);
        }

        var key = await keys.ReadAsync(tenantId, cancellationToken);
        return key is null
            ? Recording(adapters.For(bindingName), tenantId, bindingName, ownKey: false)
            : Recording(adapters.WithKey(bindingName, key.Reveal()), tenantId, bindingName, ownKey: true);
    }

    // The binding gives the canonical name and the model of the usage row. A scripted adapter answers for a name that no binding has, and records nothing.
    private IModelAdapter Recording(IModelAdapter adapter, string tenantId, string bindingName, bool ownKey) =>
        usage is not null && ai.Bindings.TryGetValue(bindingName, out var binding)
            ? new UsageRecordingModelAdapter(adapter, tenantId, binding.Name, binding.Model, ownKey, usage, time, logger)
            : adapter;
}
