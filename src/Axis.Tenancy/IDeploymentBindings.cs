namespace Axis.Tenancy;

/// <summary>Resolves the current tenant's deployment bindings only.</summary>
public interface IDeploymentBindings
{
    /// <summary>Returns the binding named <paramref name="name"/> of the current tenant, matched ignoring letter case.</summary>
    /// <exception cref="InvalidOperationException">No tenant context is set, or the tenant has no binding of that name.</exception>
    DeploymentBinding Resolve(string name);
}

/// <summary>A resolved deployment binding. <see cref="ToString"/> leaves out the value, so logging it never shows a secret.</summary>
public sealed record DeploymentBinding(string Name, string Kind, string Value)
{
    public override string ToString() => $"DeploymentBinding {{ Name = {Name}, Kind = {Kind} }}";
}
