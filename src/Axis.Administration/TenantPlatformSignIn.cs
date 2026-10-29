using System.Text.Json;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>
/// One brokered identity provider of a tenant's platform sign-in. The bindings are names of deployment bindings and never values.
/// <paramref name="Claims"/> maps a claim name (<c>displayName</c>, <c>email</c> or <c>locale</c>) to a claim of the provider, and
/// <paramref name="RoleValues"/> maps a value of <paramref name="RolesClaim"/> to a platform role. Both are lists of pairs, so that
/// a name given twice can be refused.
/// </summary>
public sealed record TenantPlatformProvider(
    string Name,
    string Type,
    string? Issuer,
    string? ClientId,
    string? ClientSecret,
    string? ClientKey,
    string? Metadata,
    IReadOnlyList<KeyValuePair<string, string>> Claims,
    string? RolesClaim,
    IReadOnlyList<KeyValuePair<string, string>> RoleValues);

/// <summary>The platform sign-in to give a tenant. It replaces the whole platform sign-in the tenant has.</summary>
public sealed record TenantPlatformSignInChange(bool Accounts, IReadOnlyList<TenantPlatformProvider> Providers);

/// <summary>
/// Sets the platform sign-in of a tenant in the tenant directory: whether the accounts of the tenant's realm may sign in, and the
/// brokered providers that are accepted besides them. The command line and a later web console both call it, so the change
/// follows the same rules everywhere. A message names options and provider names and never a value.
/// </summary>
public static class TenantPlatformSignIn
{
    private const string Conflict = "Another command changed the tenant at the same moment. Run the command again.";

    private static readonly string[] _types = ["oidc", "saml"];

    private static readonly string[] _claimNames = ["displayName", "email", "locale"];

    private static readonly string[] _roles = ["author", "publisher", "operator", "administrator"];

    /// <summary>Returns why the change is refused without reading the directory, or <see langword="null"/> when it is well formed.</summary>
    public static string? Problem(TenantPlatformSignInChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Providers.Count == 0 && !change.Accounts)
        {
            return "Without --accounts true, the platform sign-in needs at least one --provider.";
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in change.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                return "--provider must not be blank.";
            }

            if (!names.Add(provider.Name))
            {
                return $"The provider '{provider.Name}' is given more than once.";
            }

            var problem = ProviderProblem(provider);
            if (problem is not null)
            {
                return $"The provider '{provider.Name}': {problem}";
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the tenant its platform sign-in, replacing the one it had and raising the tenant's version. The audit record names
    /// <c>accounts</c> and then <c>provider:&lt;name&gt;</c> for each provider, and never a value. The tenant may be in any status,
    /// because the platform sign-in needs neither the tenant's database nor its roles. A preview tenant has none of its own.
    /// </summary>
    public static async Task<TenantSettingResult> SetAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        TenantPlatformSignInChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(change);

        var problem = Problem(change);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        var previewOf = await platform.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new { tenant.PreviewOf })
            .SingleOrDefaultAsync(cancellationToken);
        if (previewOf is null)
        {
            return TenantSettingResult.Refused(TenantSettingWrites.UnknownTenant);
        }

        if (previewOf.PreviewOf is not null)
        {
            return TenantSettingResult.Refused(
                $"The tenant '{tenantId}' is a preview tenant. Its sign-in is that of its main tenant '{previewOf.PreviewOf}'.");
        }

        string[] changed = ["accounts", .. change.Providers.Select(provider => $"provider:{provider.Name}")];
        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            changed,
            [tenantId],
            async token =>
            {
                var row = await platform.Tenants.SingleAsync(tenant => tenant.Id == tenantId, token);
                row.PlatformAccounts = change.Accounts;

                // A delete in the database, so that a provider with the same key as a new one never clashes in the change tracker.
                await platform.TenantPlatformProviders.Where(provider => provider.TenantId == tenantId).ExecuteDeleteAsync(token);
                platform.TenantPlatformProviders.AddRange(change.Providers.Select(provider => new TenantPlatformProviderRow
                {
                    TenantId = tenantId,
                    Name = provider.Name,
                    Type = provider.Type,
                    IssuerBinding = provider.Issuer,
                    ClientIdBinding = provider.ClientId,
                    ClientSecretBinding = provider.ClientSecret,
                    ClientKeyBinding = provider.ClientKey,
                    MetadataBinding = provider.Metadata,
                    ClaimDisplayName = Claim(provider, "displayName"),
                    ClaimEmail = Claim(provider, "email"),
                    ClaimLocale = Claim(provider, "locale"),
                    RolesClaim = provider.RolesClaim,
                    RoleValues = JsonSerializer.Serialize(provider.RoleValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                }));
                return null;
            },
            Conflict,
            cancellationToken);
    }

    private static string? Claim(TenantPlatformProvider provider, string name) =>
        provider.Claims.Where(pair => pair.Key == name).Select(pair => pair.Value).SingleOrDefault();

    // The messages follow the checks of an identityProvider resource, and name options and never a value.
    private static string? ProviderProblem(TenantPlatformProvider provider)
    {
        if (!_types.Contains(provider.Type, StringComparer.Ordinal))
        {
            return "--type must be oidc or saml.";
        }

        if (provider.Type == "oidc")
        {
            if (provider.Issuer is null)
            {
                return "an oidc provider needs --issuer.";
            }

            if (provider.ClientId is null)
            {
                return "an oidc provider needs --client-id.";
            }

            if ((provider.ClientSecret is null) == (provider.ClientKey is null))
            {
                return "an oidc provider needs exactly one of --client-secret and --client-key.";
            }

            if (provider.Metadata is not null)
            {
                return "an oidc provider takes no --metadata.";
            }
        }
        else
        {
            if (provider.Metadata is null)
            {
                return "a saml provider needs --metadata.";
            }

            if (provider.Issuer is not null || provider.ClientId is not null || provider.ClientSecret is not null || provider.ClientKey is not null)
            {
                return "a saml provider takes no --issuer, --client-id, --client-secret or --client-key.";
            }
        }

        string?[] bindings = [provider.Issuer, provider.ClientId, provider.ClientSecret, provider.ClientKey, provider.Metadata];
        if (bindings.Any(binding => binding is not null && string.IsNullOrWhiteSpace(binding)))
        {
            return "a binding name must not be blank.";
        }

        var claimNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, claim) in provider.Claims)
        {
            if (!_claimNames.Contains(name, StringComparer.Ordinal))
            {
                return "--claim must name displayName, email or locale.";
            }

            if (!claimNames.Add(name))
            {
                return $"The claim '{name}' is given more than once.";
            }

            if (string.IsNullOrWhiteSpace(claim))
            {
                return $"The claim '{name}' must not be blank.";
            }
        }

        if (provider.RolesClaim is not null && string.IsNullOrWhiteSpace(provider.RolesClaim))
        {
            return "--roles-claim must not be blank.";
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (value, role) in provider.RoleValues)
        {
            if (!values.Add(value))
            {
                return "A --role value is given more than once.";
            }

            if (!_roles.Contains(role, StringComparer.Ordinal))
            {
                return "--role must map to author, publisher, operator or administrator.";
            }
        }

        return null;
    }
}
