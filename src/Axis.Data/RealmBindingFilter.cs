using Axis.Configuration.Model;
using Axis.Configuration.Realms;

namespace Axis.Data;

/// <summary>
/// Plans the realm of a tenant the way activation does: a provider whose bindings the tenant lacks is left out. Activation and
/// the sign-in of a site both plan through it, so they agree on which providers a site allows.
/// </summary>
public static class RealmBindingFilter
{
    /// <summary>
    /// Plans the realm for <paramref name="applications"/> and the platform sign-in providers in <paramref name="bindings"/>,
    /// without the <c>oidc</c> providers that the tenant lacks a binding for.
    /// </summary>
    public static RealmPlanResult Plan(IEnumerable<RealmApplication> applications, ActivationBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(bindings);

        return RealmPlanner.Build(
            [.. applications.Select(application => application with
            {
                Providers = [.. application.Providers.Where(provider => IsBound(provider, bindings))],
            })],
            [.. bindings.PlatformProviders.Where(provider => IsBound(provider, bindings))]);
    }

    /// <summary>Whether the tenant has every binding of an <c>oidc</c> provider with the right kind, ignoring letter case.</summary>
    public static bool IsBound(IdentityProviderModel provider, ActivationBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(bindings);

        if (provider.Type != "oidc")
        {
            return true;
        }

        bool Has(string? name, string kind) =>
            name is null
            || bindings.Bindings.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.Kind == kind;

        return provider.IssuerBinding is not null
            && Has(provider.IssuerBinding, ActivationBinding.UrlKind)
            && Has(provider.ClientIdBinding, ActivationBinding.SecretKind)
            && Has(provider.ClientSecretBinding, ActivationBinding.SecretKind)
            && Has(provider.ClientKeyBinding, ActivationBinding.SecretKind);
    }
}
