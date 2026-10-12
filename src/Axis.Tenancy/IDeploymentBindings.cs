namespace Axis.Tenancy;

/// <summary>Resolves the current tenant's deployment bindings only.</summary>
public interface IDeploymentBindings
{
    /// <summary>
    /// Returns the binding named <paramref name="name"/> of the current tenant, matched ignoring letter case. A
    /// <c>secret</c> binding is refused, so no plain secret string is handed out: use <see cref="ResolveSecret"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No tenant context is set, the tenant has no binding of that name, or the binding is a secret.
    /// </exception>
    DeploymentBinding Resolve(string name);

    /// <summary>
    /// Returns the value of the <c>secret</c> binding named <paramref name="name"/> of the current tenant, matched
    /// ignoring letter case. The value shows only through <see cref="SecretValue.Reveal"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No tenant context is set, the tenant has no binding of that name, or the binding is not a secret.
    /// </exception>
    SecretValue ResolveSecret(string name);
}

/// <summary>A resolved deployment binding. <see cref="ToString"/> leaves out the value, so logging it never shows a secret.</summary>
public sealed record DeploymentBinding(string Name, string Kind, string Value)
{
    public override string ToString() => $"DeploymentBinding {{ Name = {Name}, Kind = {Kind} }}";
}
