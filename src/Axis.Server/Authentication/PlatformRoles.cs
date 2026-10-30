using Axis.Server.Studio;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Server.Authentication;

/// <summary>
/// Applies a tenant's <see cref="PlatformSignIn"/> to one sign-in: whether the platform pages accept it, and which
/// platform roles the provider's mapping gives the person.
/// </summary>
internal static class PlatformRoles
{
    private const string Author = "author";
    private const string Publisher = "publisher";
    private const string Operator = "operator";
    private const string Administrator = "administrator";

    /// <summary>The kind of the administrator role, as the give-role and remove-role routes name it.</summary>
    public const string AdministratorKind = Administrator;

    /// <summary>The kinds of the four platform roles.</summary>
    public static IReadOnlyList<string> Kinds => [Author, Publisher, Operator, Administrator];

    /// <summary>Whether <paramref name="role"/> is the kind of one of the four platform roles.</summary>
    public static bool IsKind(string role) => role is Author or Publisher or Operator or Administrator;

    /// <summary>
    /// The role name that the host settings configure for a platform role kind, or <c>null</c> when the kind is unknown or its
    /// setting has no value.
    /// </summary>
    public static string? NameOf(string kind, StudioOptions studio, ConsoleSettings console, UsersSettings users) => kind switch
    {
        Author => studio.AuthorRole,
        Publisher => studio.PublisherRole,
        Operator => console.OperatorRole,
        Administrator => users.AdministratorRole,
        _ => null,
    };

    /// <summary>
    /// Decides a sign-in. A sign-in without an <c>identity_provider</c> claim is an account sign-in, which the platform
    /// sign-in accepts only with accounts on. A sign-in through a provider is accepted only when a provider of the
    /// platform sign-in has exactly that alias.
    /// </summary>
    /// <param name="claimValues">Reads the values of a claim of the ID token.</param>
    /// <returns>
    /// The role names the host settings configure for the platform roles that the provider's roles claim values map to,
    /// without repeats, or <c>null</c> when the sign-in is not accepted. An account sign-in maps no role.
    /// </returns>
    public static IReadOnlyList<string>? Resolve(
        PlatformSignIn signIn,
        string? identityProvider,
        Func<string, IEnumerable<string>> claimValues,
        StudioOptions studio,
        ConsoleSettings console,
        UsersSettings users)
    {
        if (string.IsNullOrEmpty(identityProvider))
        {
            return signIn.Accounts ? [] : null;
        }

        var provider = signIn.Providers.FirstOrDefault(candidate => string.Equals(candidate.Alias, identityProvider, StringComparison.Ordinal));
        if (provider is null)
        {
            return null;
        }

        if (provider.RolesClaim is null)
        {
            return [];
        }

        var roles = new List<string>();
        foreach (var value in claimValues(RealmSync.RolesAttribute(provider.RolesClaim)))
        {
            if (!provider.RoleValues.TryGetValue(value, out var platformRole))
            {
                continue;
            }

            var name = NameOf(platformRole, studio, console, users);
            if (name is not null && !roles.Contains(name, StringComparer.Ordinal))
            {
                roles.Add(name);
            }
        }

        return roles;
    }
}
