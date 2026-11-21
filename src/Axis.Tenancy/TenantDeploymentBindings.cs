namespace Axis.Tenancy;

/// <summary>
/// Reads one tenant's bindings from a map that the caller already holds, matched ignoring letter case as the map's comparer says. The worker's
/// <see cref="DeploymentBindings"/> and the publisher both resolve through it. No message names a binding's value.
/// </summary>
public sealed class TenantDeploymentBindings(string tenantId, IReadOnlyDictionary<string, TenantBinding> bindings) : IDeploymentBindings
{
    public DeploymentBinding Resolve(string name)
    {
        var binding = Find(name);
        return binding.Kind == DeploymentBindingKinds.Secret
            ? throw new InvalidOperationException($"Tenant '{tenantId}' binding '{name}' is a secret: resolve it with ResolveSecret.")
            : new DeploymentBinding(name, binding.Kind, binding.Url!);
    }

    public SecretValue ResolveSecret(string name)
    {
        var binding = Find(name);
        return binding.Kind == DeploymentBindingKinds.Secret
            ? binding.Secret!
            : throw new InvalidOperationException($"Tenant '{tenantId}' binding '{name}' is not a secret.");
    }

    private TenantBinding Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return bindings.TryGetValue(name, out var binding)
            ? binding
            : throw new InvalidOperationException($"Tenant '{tenantId}' has no deployment binding '{name}'.");
    }
}
