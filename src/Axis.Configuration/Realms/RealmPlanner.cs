using Axis.Configuration.Model;

namespace Axis.Configuration.Realms;

/// <summary>Why two identity providers cannot share one realm.</summary>
public enum RealmConflictKind
{
    /// <summary>The same issuer binding with other client bindings.</summary>
    ClientBindings,

    /// <summary>The same bindings with other claims.</summary>
    Claims,
}

/// <summary>
/// A provider of an application that cannot share the realm with <paramref name="OtherProvider"/> of
/// <paramref name="Other"/>, because the same person would become two users.
/// </summary>
public sealed record RealmConflict(
    RealmConflictKind Kind,
    Guid ApplicationId,
    IdentityProviderModel Provider,
    RealmApplication Other,
    IdentityProviderModel OtherProvider);

/// <summary>The plan and the conflicts found while building it. A plan with conflicts must not be applied.</summary>
public sealed record RealmPlanResult(RealmPlan Plan, IReadOnlyList<RealmConflict> Conflicts);

/// <summary>Builds the <see cref="RealmPlan"/> of a set of applications. It reads nothing and changes nothing.</summary>
public static class RealmPlanner
{
    private const string Accounts = "accounts";
    private const string Oidc = "oidc";

    private static readonly IdentityProviderClaimsModel _defaultClaims = new("name", "email", "locale");

    /// <summary>
    /// Plans the realm for <paramref name="applications"/>. Only <c>oidc</c> providers are planned. Providers that name
    /// the same bindings, ignoring letter case, become one provider. Providers that name the same issuer binding
    /// with other client bindings, or the same bindings with other claims, are conflicts.
    /// </summary>
    public static RealmPlanResult Build(IReadOnlyList<RealmApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        var members = applications
            .SelectMany(application => application.Providers
                .Where(provider => provider is { Type: Oidc, IssuerBinding: not null, ClientIdBinding: not null })
                .Select(provider => new Member(application, provider)))
            .OrderBy(member => member.Application.ApplicationId)
            .ThenBy(member => member.Provider.Id)
            .ToList();

        var groups = members
            .GroupBy(member => GroupKey(member.Provider), StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();

        var conflicts = new List<RealmConflict>();
        foreach (var issuerGroups in groups.GroupBy(group => group[0].Provider.IssuerBinding!.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var clashing = issuerGroups.ToList();
            foreach (var group in clashing)
            {
                var other = clashing.FirstOrDefault(candidate => candidate != group)?[0];
                if (other is null)
                {
                    continue;
                }

                conflicts.AddRange(group.Select(member => ToConflict(RealmConflictKind.ClientBindings, member, other)));
            }
        }

        foreach (var group in groups)
        {
            foreach (var member in group)
            {
                var other = group.FirstOrDefault(candidate => ClaimsOf(candidate.Provider) != ClaimsOf(member.Provider));
                if (other is not null)
                {
                    conflicts.Add(ToConflict(RealmConflictKind.Claims, member, other));
                }
            }
        }

        var providers = groups
            .Select(group => ToProvider(group))
            .DistinctBy(provider => provider.Alias, StringComparer.Ordinal)
            .OrderBy(provider => provider.Alias, StringComparer.Ordinal)
            .ToList();
        var aliases = new HashSet<string>(providers.Select(provider => provider.Alias), StringComparer.Ordinal);

        var aliasesByProvider = groups
            .SelectMany(group => group.Select(member => (Key: (member.Application.ApplicationId, member.Provider.Id), Alias: Alias(group[0].Provider))))
            .ToDictionary(entry => entry.Key, entry => entry.Alias);

        var sites = applications
            .SelectMany(application => application.Sites.Select(site => ToSiteClient(application, site, aliasesByProvider, aliases)))
            .OrderBy(site => site.Path, StringComparer.Ordinal)
            .ToList();

        return new RealmPlanResult(new RealmPlan(sites, providers), conflicts);
    }

    private static RealmSiteClient ToSiteClient(
        RealmApplication application,
        RealmApplicationSite site,
        Dictionary<(Guid ApplicationId, Guid ProviderId), string> aliasesByProvider,
        HashSet<string> aliases)
    {
        var allowed = site.SignIn is null
            ? application.Providers
            : [.. site.SignIn.Select(name => application.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase))).OfType<IdentityProviderModel>()];

        var siteAliases = allowed
            .Select(provider => aliasesByProvider.GetValueOrDefault((application.ApplicationId, provider.Id)))
            .Where(alias => alias is not null && aliases.Contains(alias))
            .Select(alias => alias!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new RealmSiteClient(site.Path, allowed.Any(provider => provider.Type == Accounts), siteAliases);
    }

    private static RealmProvider ToProvider(List<Member> group)
    {
        var first = group[0].Provider;
        return new RealmProvider(
            Alias(first),
            first.IssuerBinding!,
            first.ClientIdBinding!,
            first.ClientSecretBinding,
            first.ClientKeyBinding,
            ClaimsOf(first),
            [.. group.Select(member => member.Provider.Roles.Claim).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    private static RealmConflict ToConflict(RealmConflictKind kind, Member member, Member other) =>
        new(kind, member.Application.ApplicationId, member.Provider, other.Application, other.Provider);

    private static string Alias(IdentityProviderModel provider) => "oidc-" + provider.IssuerBinding!.ToLowerInvariant();

    private static IdentityProviderClaimsModel ClaimsOf(IdentityProviderModel provider) => provider.Claims ?? _defaultClaims;

    private static string GroupKey(IdentityProviderModel provider) =>
        string.Join(
            '\n',
            provider.IssuerBinding!.ToLowerInvariant(),
            provider.ClientIdBinding!.ToLowerInvariant(),
            provider.ClientSecretBinding?.ToLowerInvariant() ?? "",
            provider.ClientKeyBinding?.ToLowerInvariant() ?? "");

    private sealed record Member(RealmApplication Application, IdentityProviderModel Provider);
}
