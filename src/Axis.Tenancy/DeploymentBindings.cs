using Axis.Core.Tenancy;

namespace Axis.Tenancy;

/// <summary>
/// Reads the current tenant's bindings from the tenant options on each call. No message names a binding's value.
/// </summary>
public sealed class DeploymentBindings(TenantOptions options, ITenantContextAccessor accessor) : IDeploymentBindings
{
    public DeploymentBinding Resolve(string name)
    {
        var (tenantId, binding) = Find(name);
        return binding.Kind == DeploymentBindingKinds.Secret
            ? throw new InvalidOperationException($"Tenant '{tenantId}' binding '{name}' is a secret: resolve it with ResolveSecret.")
            : new DeploymentBinding(name, binding.Kind!, binding.Value!);
    }

    public SecretValue ResolveSecret(string name)
    {
        var (tenantId, binding) = Find(name);
        return binding.Kind == DeploymentBindingKinds.Secret
            ? new SecretValue(binding.Value!)
            : throw new InvalidOperationException($"Tenant '{tenantId}' binding '{name}' is not a secret.");
    }

    private (string TenantId, DeploymentBindingSettings Binding) Find(string name)
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
            : (tenant.TenantId, binding);
    }
}
