using Axis.Configuration.Model;

namespace Axis.Tenancy.Realms;

/// <summary>Checks that the deployment bindings of an <c>oidc</c> or <c>saml</c> identity provider exist, have the right kind and hold a value.</summary>
public static class RealmProviderBindings
{
    /// <summary>
    /// The name of the first binding of an <c>oidc</c> or <c>saml</c> provider that the tenant lacks, that is of the wrong kind or that has no value,
    /// ignoring letter case, or <see langword="null"/> when all are set. The issuer must be a <c>url</c> binding, and the client
    /// bindings <c>secret</c> bindings. The metadata of a <c>saml</c> provider must be a <c>url</c> binding. Any other provider has none to check.
    /// </summary>
    public static string? Missing(IdentityProviderModel provider, IReadOnlyDictionary<string, TenantBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(bindings);

        bool Has(string name, string kind) =>
            bindings.TryGetValue(name, out var binding)
            && binding.Kind == kind
            && (kind == DeploymentBindingKinds.Secret ? binding.Secret is not null : !string.IsNullOrWhiteSpace(binding.Url));

        if (provider.Type == "saml")
        {
            return provider.MetadataBinding is { } metadata && !Has(metadata, DeploymentBindingKinds.Url) ? metadata : null;
        }

        if (provider.Type != "oidc")
        {
            return null;
        }

        if (provider.IssuerBinding is { } issuer && !Has(issuer, DeploymentBindingKinds.Url))
        {
            return issuer;
        }

        foreach (var name in new[] { provider.ClientIdBinding, provider.ClientSecretBinding, provider.ClientKeyBinding })
        {
            if (name is not null && !Has(name, DeploymentBindingKinds.Secret))
            {
                return name;
            }
        }

        return null;
    }
}
