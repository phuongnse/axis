using Axis.Configuration.Realms;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Makes a tenant's realm follow the realm plan of its applications. It resolves the plan's binding names against the
/// tenant's deployment bindings into a <see cref="RealmState"/> and applies it with <see cref="RealmSync"/>. An error
/// names the provider alias and the binding, never a value.
/// </summary>
public static class ActivationRealmSync
{
    /// <summary>
    /// Resolves <paramref name="plan"/> against <paramref name="bindings"/>: the sites, and for each provider its
    /// issuer, client id, client secret and claims.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A provider has no <c>clientSecret</c> binding, or a binding it names is missing or of the wrong kind. The message
    /// names the provider alias and, for a binding, its name.
    /// </exception>
    public static RealmState ToState(RealmPlan plan, IReadOnlyDictionary<string, TenantBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(bindings);

        return new RealmState(
            [.. plan.Sites],
            [.. plan.Providers.Select(provider => ToProvider(provider, bindings))]);
    }

    /// <summary>
    /// Syncs the tenant's realm to <paramref name="plan"/>. It does nothing when the tenant has no realm details.
    /// </summary>
    public static Task SyncAsync(
        HttpClient http,
        TenantRealmAccess? realm,
        IReadOnlyDictionary<string, TenantBinding> bindings,
        RealmPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (realm is null)
        {
            return Task.CompletedTask;
        }

        return new RealmSync(http, realm.BaseAddress)
            .SyncAsync(realm.Name, realm.ActivationClientSecret.Reveal(), ToState(plan, bindings) with { PlatformClientSecret = realm.PlatformClientSecret?.Reveal() }, cancellationToken);
    }

    private static RealmBrokeredProvider ToProvider(RealmProvider provider, IReadOnlyDictionary<string, TenantBinding> bindings)
    {
        var alias = provider.Alias;
        if (provider.ClientSecretBinding is not { } clientSecretBinding)
        {
            throw new InvalidOperationException($"The identity provider '{alias}' has no clientSecret binding, which the realm needs.");
        }

        return new RealmBrokeredProvider(
            alias,
            new Uri(Url(alias, provider.IssuerBinding, bindings)),
            Secret(alias, provider.ClientIdBinding, bindings),
            Secret(alias, clientSecretBinding, bindings),
            provider.Claims.DisplayName,
            provider.Claims.Email,
            provider.Claims.Locale,
            provider.RolesClaims);
    }

    private static string Url(string alias, string name, IReadOnlyDictionary<string, TenantBinding> bindings) =>
        Find(alias, name, DeploymentBindingKinds.Url, bindings).Url
        ?? throw new InvalidOperationException($"The binding '{name}' of the identity provider '{alias}' has no URL.");

    private static string Secret(string alias, string name, IReadOnlyDictionary<string, TenantBinding> bindings) =>
        Find(alias, name, DeploymentBindingKinds.Secret, bindings).Secret?.Reveal()
        ?? throw new InvalidOperationException($"The binding '{name}' of the identity provider '{alias}' has no value.");

    private static TenantBinding Find(string alias, string name, string kind, IReadOnlyDictionary<string, TenantBinding> bindings)
    {
        if (!bindings.TryGetValue(name, out var binding))
        {
            throw new InvalidOperationException($"The identity provider '{alias}' needs the binding '{name}', which the tenant does not have.");
        }

        return binding.Kind == kind
            ? binding
            : throw new InvalidOperationException($"The binding '{name}' of the identity provider '{alias}' must be of kind '{kind}', not '{binding.Kind}'.");
    }
}
