using Axis.Configuration.Model;
using Axis.Tenancy.Realms;

namespace Axis.Server.Authentication;

/// <summary>
/// Works out, for one sign-in, the roles that each application's identity providers give the person through their
/// <c>roles</c> mapping. Each application has its own result, so two applications can map the same claim value to
/// different roles.
/// </summary>
internal static class ApplicationRoles
{
    /// <summary>
    /// Maps the roles claim values of a sign-in through a provider to the roles of each application. An application reads
    /// the providers whose realm alias is <paramref name="identityProvider"/>, and gets the union of their roles.
    /// </summary>
    /// <param name="claimValues">Reads the values of a claim of the ID token.</param>
    /// <returns>
    /// The role names for each application id, without repeats. A claim value gives the role the provider's <c>values</c>
    /// map it to, or the role of the same name when the provider has no <c>values</c>. A value that gives no declared role is
    /// ignored. An application that gets no role is left out, and so is every application for an account sign-in, which has
    /// no <paramref name="identityProvider"/>, or for an alias that no provider has.
    /// </returns>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<string>> Resolve(
        IEnumerable<ApplicationModel> applications,
        string? identityProvider,
        Func<string, IEnumerable<string>> claimValues)
    {
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(identityProvider))
        {
            return result;
        }

        foreach (var application in applications)
        {
            var roles = new List<string>();
            foreach (var provider in application.IdentityProviders.Where(
                candidate => string.Equals(SiteSignIn.AliasOf(candidate), identityProvider, StringComparison.Ordinal)))
            {
                foreach (var value in claimValues(RealmSync.RolesAttribute(provider.Roles.Claim)))
                {
                    string? role = value;
                    if (provider.Roles.Values.Count > 0 && !provider.Roles.Values.TryGetValue(value, out role))
                    {
                        continue;
                    }

                    if (application.Roles.Any(declared => string.Equals(declared.Name, role, StringComparison.Ordinal))
                        && !roles.Contains(role, StringComparer.Ordinal))
                    {
                        roles.Add(role);
                    }
                }
            }

            if (roles.Count > 0)
            {
                result[application.Manifest.Id] = roles;
            }
        }

        return result;
    }
}
