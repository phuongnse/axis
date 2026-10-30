using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the identity providers of an application and builds the checked ones. A <c>type</c> is
/// <c>accounts</c>, <c>oidc</c> or <c>saml</c>. An <c>oidc</c> provider has an <c>issuer</c>, a
/// <c>clientId</c> and a <c>clientSecret</c> or a <c>clientKey</c>, and a <c>saml</c> provider has
/// <c>metadata</c>. A binding name that the application also uses as another kind is a binding of
/// the wrong kind. No resource declares binding kinds, so the kind of a name is read from where it
/// is used: a connector's <c>baseUrl</c> and <c>oauth2</c> <c>tokenUrl</c> are <c>url</c>, its other <c>auth</c> bindings <c>secret</c>, the
/// manifest's <c>fileStorage</c> a <c>storage</c>, and a provider's own bindings as the doc says.
/// Names are compared ignoring letter case. A role in <c>roles.values</c> is a declared role. An
/// application has at most one <c>accounts</c> provider. The schema already refuses the properties
/// that a type does not take. A site's <c>signIn</c> names providers of the application, compared
/// ignoring letter case, and an application with sites needs at least one provider. Every problem of a provider is reported in one pass. Its bindings are
/// not checked against the tenant's here, activation does that.
/// </summary>
internal static class IdentityProviderChecker
{
    private const string Accounts = "accounts";
    private const string Oidc = "oidc";
    private const string Saml = "saml";

    private const string UrlKind = BindingUses.UrlKind;
    private const string SecretKind = BindingUses.SecretKind;

    private const string DefaultRolesClaim = "roles";

    /// <summary>Checks <paramref name="providers"/>, which come in path order.</summary>
    public static void Check(
        IReadOnlyList<IdentityProviderResource> providers,
        ApplicationManifest? manifest,
        IReadOnlyList<ConnectorResource> connectors,
        Func<string, RoleResource?> findRole,
        IReadOnlySet<string> unloadedRoleNames,
        List<Diagnostic> diagnostics)
    {
        if (providers.Count == 0)
        {
            return;
        }

        var uses = BindingUses.Collect(providers, manifest, connectors);
        IdentityProviderResource? firstAccounts = null;
        foreach (var provider in providers)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, provider.File, path, provider.Id));

            if (!IsKnownType(provider.Type))
            {
                Report(
                    DiagnosticCodes.UnknownIdentityProviderType,
                    $"The type '{provider.Type}' is not allowed. Use '{Accounts}', '{Oidc}' or '{Saml}'.",
                    "/type");
            }
            else
            {
                CheckMissingBindings(provider, Report);
                CheckBindingKinds(provider, uses, Report);
            }

            if (provider.Roles is { } roles)
            {
                foreach (var (claimValue, role) in roles.Values)
                {
                    if (findRole(role) is null && !unloadedRoleNames.Contains(role))
                    {
                        Report(
                            DiagnosticCodes.UnknownIdentityProviderRole,
                            $"The claim value '{claimValue}' gives the role '{role}', which this application does not declare.",
                            $"/roles/values/{EscapePointerSegment(claimValue)}");
                    }
                }
            }

            if (provider.Type == Accounts)
            {
                if (firstAccounts is null)
                {
                    firstAccounts = provider;
                }
                else
                {
                    Report(
                        DiagnosticCodes.SecondAccountsProvider,
                        $"An application has at most one '{Accounts}' provider, and '{firstAccounts.File}' is one already.",
                        "/type");
                }
            }
        }
    }

    /// <summary>
    /// Checks that each site has a provider to sign in with, and that each <c>signIn</c> entry names one.
    /// A name in <paramref name="unloadedProviderNames"/> counts as a provider, so one broken file gives one error.
    /// </summary>
    public static void CheckSites(
        IReadOnlyList<SiteResource> sites,
        IReadOnlyList<IdentityProviderResource> providers,
        IReadOnlySet<string> unloadedProviderNames,
        List<Diagnostic> diagnostics)
    {
        if (providers.Count == 0 && unloadedProviderNames.Count == 0)
        {
            foreach (var site in sites)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.SiteWithoutIdentityProvider,
                    $"An application with sites needs at least one identity provider, such as an '{Accounts}' provider.",
                    site.File,
                    string.Empty,
                    site.Id));
            }

            return;
        }

        var names = new HashSet<string>(providers.Select(provider => provider.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites)
        {
            for (var i = 0; site.SignIn is { } signIn && i < signIn.Count; i++)
            {
                if (!names.Contains(signIn[i]) && !unloadedProviderNames.Contains(signIn[i]))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.UnknownSignInProvider,
                        $"The identity provider '{signIn[i]}' is not declared in this application.",
                        site.File,
                        $"/signIn/{i}",
                        site.Id));
                }
            }
        }
    }

    /// <summary>Builds a checked identity provider, with the claim defaults filled in.</summary>
    public static IdentityProviderModel Build(IdentityProviderResource provider)
    {
        if (!IsKnownType(provider.Type))
        {
            throw new ArgumentException("Only a checked identity provider can be built.", nameof(provider));
        }

        return new IdentityProviderModel
        {
            Id = provider.Id,
            Name = provider.Name,
            File = provider.File,
            Type = provider.Type,
            LabelTextKey = provider.Label?.TextKey,
            IssuerBinding = provider.Issuer?.Binding,
            ClientIdBinding = provider.ClientId?.Binding,
            ClientSecretBinding = provider.ClientSecret?.Binding,
            ClientKeyBinding = provider.ClientKey?.Binding,
            MetadataBinding = provider.Metadata?.Binding,
            Claims = provider.Type == Accounts
                ? null
                : new IdentityProviderClaimsModel(
                    provider.Claims?.DisplayName ?? "name",
                    provider.Claims?.Email ?? "email",
                    provider.Claims?.Locale ?? "locale"),
            Roles = new IdentityProviderRolesModel(
                provider.Roles?.Claim ?? DefaultRolesClaim,
                provider.Roles?.Values ?? new Dictionary<string, string>()),
            Acr = provider.Acr,
        };
    }

    private static bool IsKnownType(string type) => type is Accounts or Oidc or Saml;

    /// <summary>Reports each binding that an <c>oidc</c> or <c>saml</c> provider needs and lacks, at <c>/type</c>.</summary>
    private static void CheckMissingBindings(IdentityProviderResource provider, Action<string, string, string> report)
    {
        void Missing(string need) =>
            report(
                DiagnosticCodes.MissingIdentityProviderBinding,
                $"An '{provider.Type}' provider needs {need}.",
                "/type");

        switch (provider.Type)
        {
            case Oidc:
                if (provider.Issuer is null)
                {
                    Missing("an 'issuer' binding");
                }

                if (provider.ClientId is null)
                {
                    Missing("a 'clientId' binding");
                }

                if (provider.ClientSecret is null && provider.ClientKey is null)
                {
                    Missing("a 'clientSecret' or a 'clientKey' binding");
                }

                break;
            case Saml:
                if (provider.Metadata is null)
                {
                    Missing("a 'metadata' binding");
                }

                break;
        }
    }

    /// <summary>Reports each binding whose name the application also uses as another kind, at <c>/&lt;property&gt;/binding</c>.</summary>
    private static void CheckBindingKinds(
        IdentityProviderResource provider,
        Dictionary<string, List<BindingUse>> uses,
        Action<string, string, string> report)
    {
        foreach (var (property, kind, binding) in BindingsOf(provider))
        {
            if (uses.TryGetValue(binding.Binding, out var candidates)
                && candidates.FirstOrDefault(candidate => candidate.Kind != kind) is { } other)
            {
                report(
                    DiagnosticCodes.IdentityProviderBindingKind,
                    $"The binding '{binding.Binding}' is a {kind} binding here, but '{other.File}' uses it as a {other.Kind} binding at '{other.Path}'.",
                    $"/{property}/binding");
            }
        }
    }

    internal static IEnumerable<(string Property, string Kind, BindingReference Binding)> BindingsOf(IdentityProviderResource provider)
    {
        if (provider.Issuer is { } issuer)
        {
            yield return ("issuer", UrlKind, issuer);
        }

        if (provider.Metadata is { } metadata)
        {
            yield return ("metadata", UrlKind, metadata);
        }

        if (provider.ClientId is { } clientId)
        {
            yield return ("clientId", SecretKind, clientId);
        }

        if (provider.ClientSecret is { } clientSecret)
        {
            yield return ("clientSecret", SecretKind, clientSecret);
        }

        if (provider.ClientKey is { } clientKey)
        {
            yield return ("clientKey", SecretKind, clientKey);
        }
    }

    /// <summary>Escapes a claim value as an RFC 6901 JSON Pointer segment.</summary>
    private static string EscapePointerSegment(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

}
