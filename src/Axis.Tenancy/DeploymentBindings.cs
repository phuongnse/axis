using Axis.Core.Tenancy;

namespace Axis.Tenancy;

/// <summary>Reads the current tenant's bindings from the tenant options on each call.</summary>
public sealed class DeploymentBindings(TenantOptions options, ITenantContextAccessor accessor) : IDeploymentBindings
{
    public DeploymentBinding Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var tenant = accessor.Current
            ?? throw new InvalidOperationException(
                $"No tenant context is set. Deployment binding '{name}' can only be resolved while a tenant is resolved.");

        DeploymentBindingSettings? binding = null;
        if (options.Tenants.TryGetValue(tenant.TenantId, out var settings))
        {
            settings?.Bindings.TryGetValue(name, out binding);
        }

        return binding is null
            ? throw new InvalidOperationException($"Tenant '{tenant.TenantId}' has no deployment binding '{name}'.")
            : new DeploymentBinding(name, binding.Kind!, binding.Value!);
    }
}
