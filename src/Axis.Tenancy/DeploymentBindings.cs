using Axis.Core.Tenancy;

namespace Axis.Tenancy;

/// <summary>
/// Reads the current tenant's bindings from the tenant catalog on each call, through <see cref="TenantDeploymentBindings"/>. No message names a
/// binding's value.
/// </summary>
public sealed class DeploymentBindings(ITenantCatalog catalog, ITenantContextAccessor accessor) : IDeploymentBindings
{
    private static readonly IReadOnlyDictionary<string, TenantBinding> _none = new Dictionary<string, TenantBinding>();

    public DeploymentBinding Resolve(string name) => Current(name).Resolve(name);

    public SecretValue ResolveSecret(string name) => Current(name).ResolveSecret(name);

    private TenantDeploymentBindings Current(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var tenant = accessor.Current
            ?? throw new InvalidOperationException(
                $"No tenant context is set. Deployment binding '{name}' can only be resolved while a tenant is resolved.");

        return new TenantDeploymentBindings(tenant.TenantId, catalog.Find(tenant.TenantId)?.Bindings ?? _none);
    }
}
