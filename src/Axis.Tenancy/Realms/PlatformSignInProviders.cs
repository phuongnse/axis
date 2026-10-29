using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Tenancy.Platform;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Reads the brokered providers of a tenant's platform sign-in as <see cref="IdentityProviderModel"/>, so that
/// <see cref="Axis.Configuration.Realms.RealmPlanner"/> can plan them with the providers of the applications.
/// </summary>
public static class PlatformSignInProviders
{
    /// <summary>
    /// The providers of <paramref name="rows"/>, ordered by name. A provider's id is the first 16 bytes of the SHA-256 of its lower-case name,
    /// so it is unique among the platform providers and stays the same. The claims and the roles claim take the defaults of an
    /// <c>identityProvider</c> resource when the row leaves them out. A provider has no file.
    /// </summary>
    public static IReadOnlyList<IdentityProviderModel> From(IEnumerable<TenantPlatformProviderRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return [.. rows
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .Select(row => new IdentityProviderModel
            {
                Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(row.Name.ToLowerInvariant())).AsSpan(0, 16)),
                Name = row.Name,
                File = "",
                Type = row.Type,
                IssuerBinding = row.IssuerBinding,
                ClientIdBinding = row.ClientIdBinding,
                ClientSecretBinding = row.ClientSecretBinding,
                ClientKeyBinding = row.ClientKeyBinding,
                MetadataBinding = row.MetadataBinding,
                Claims = new IdentityProviderClaimsModel(row.ClaimDisplayName ?? "name", row.ClaimEmail ?? "email", row.ClaimLocale ?? "locale"),
                Roles = new IdentityProviderRolesModel(
                    row.RolesClaim ?? "roles",
                    JsonSerializer.Deserialize<Dictionary<string, string>>(row.RoleValues) ?? []),
                Acr = [],
            })];
    }
}
