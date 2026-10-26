using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>
/// The sign-in settings to change. <see langword="null"/> means the option was not given. An empty audience or claim value
/// removes the stored one. <paramref name="ClientKey"/> is the PEM of a client key to add and <paramref name="RetireClientKeyId"/>
/// the key id of a client key to remove.
/// </summary>
public sealed record TenantSignInChange(
    string? Authority,
    string? ClientId,
    string? ClientSecret,
    IReadOnlyList<KeyValuePair<string, string>> Claims,
    string? EndpointAudience,
    string? ServiceAudience,
    string? Profile,
    string? ClientKey = null,
    string? RetireClientKeyId = null);

/// <summary>What <see cref="TenantSignInSettings.SetAsync"/> did.</summary>
public enum TenantSignInOutcome
{
    Changed,
    Refused,
    AuditFailed,
}

/// <summary>
/// The result of a change. <paramref name="Refusal"/> holds the message of a refusal, <paramref name="AuditError"/> the error of a
/// record that could not be written and <paramref name="Changed"/> the names of the settings that were changed.
/// <paramref name="AddedKeyId"/> and <paramref name="AddedPublicKeyPem"/> describe the client key that was added, if any.
/// </summary>
public sealed record TenantSignInResult(
    TenantSignInOutcome Outcome,
    string? Refusal,
    Exception? AuditError,
    IReadOnlyList<string> Changed,
    string? AddedKeyId = null,
    string? AddedPublicKeyPem = null);

/// <summary>
/// Changes the sign-in settings of a main tenant in the tenant directory. The command line and a later web console both call
/// it, so the change follows the same rules everywhere. Every refusal is decided before the platform audit record, so a refusal
/// changes nothing. A message names options and never a value.
/// </summary>
public static class TenantSignInSettings
{
    private static readonly string[] _claimNames = ["UserId", "DisplayName", "Roles", "Email", "Locale"];

    /// <summary>Returns why the change is refused without reading the directory, or <see langword="null"/> when it is well formed.</summary>
    public static string? Problem(TenantSignInChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Authority is null && change.ClientId is null && change.ClientSecret is null && change.Claims.Count == 0
            && change.EndpointAudience is null && change.ServiceAudience is null && change.Profile is null
            && change.ClientKey is null && change.RetireClientKeyId is null)
        {
            return "Give at least one sign-in setting.";
        }

        if (change.Authority is not null
            && (!Uri.TryCreate(change.Authority, UriKind.Absolute, out var authority)
                || (authority.Scheme != Uri.UriSchemeHttp && authority.Scheme != Uri.UriSchemeHttps)))
        {
            return "--authority must be an absolute http or https URL.";
        }

        if (change.ClientId is not null && string.IsNullOrWhiteSpace(change.ClientId))
        {
            return "--client-id must not be blank.";
        }

        if (change.ClientSecret is { Length: 0 })
        {
            return "The client secret must not be empty.";
        }

        if (change.ClientKey is { Length: 0 })
        {
            return "The client key must not be empty.";
        }

        if (change.ClientKey is not null && !TenantClientKeys.TryRead(change.ClientKey, out _, out _, out _))
        {
            return "The client key must be an EC P-256 private key as a PEM.";
        }

        if (change.RetireClientKeyId is not null && string.IsNullOrWhiteSpace(change.RetireClientKeyId))
        {
            return "--retire-client-key must not be blank.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in change.Claims)
        {
            if (!_claimNames.Contains(name, StringComparer.Ordinal))
            {
                return $"The claim name '{name}' is unknown. Use UserId, DisplayName, Roles, Email or Locale.";
            }

            if (!seen.Add(name))
            {
                return $"The claim name '{name}' is given more than once.";
            }
        }

        if (change.Profile is not null && change.Profile is not (TenantSignInProfiles.Standard or TenantSignInProfiles.Fapi2))
        {
            return $"--profile must be '{TenantSignInProfiles.Standard}' or '{TenantSignInProfiles.Fapi2}'.";
        }

        return null;
    }

    /// <summary>
    /// Changes the settings of the tenant. The record names every setting that was given, in a fixed order, and is written before
    /// the change, so a record that cannot be written leaves the tenant as it was. The change raises the tenant <c>version</c>
    /// once, so a server rebuilds the tenant's sign-in client at its next refresh. A tenant holds at most two client keys, the newest
    /// first, and the checks look at the tenant as it will be after the whole change. The record names a key id and never a key.
    /// A database error propagates.
    /// </summary>
    public static async Task<TenantSignInResult> SetAsync(
        TenantSecretCipher cipher,
        string platformConnectionString,
        string command,
        string tenantId,
        TenantSignInChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(change);

        var problem = Problem(change);
        if (problem is not null)
        {
            return Refused(problem);
        }

        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
        var row = await platform.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
        if (row is null)
        {
            return Refused("The tenant does not exist.");
        }

        if (row.PreviewOf is not null)
        {
            return Refused($"The tenant '{tenantId}' is a preview tenant. It takes the sign-in settings of its main tenant '{row.PreviewOf}'.");
        }

        // The keys newest first, as the key id and the encrypted key. A key that is added has no encrypted form until the record is written.
        var keys = new List<(string KeyId, byte[]? Encrypted)>();
        if (row.OidcClientKey is { Length: > 0 })
        {
            keys.Add((row.OidcClientKeyId ?? "", row.OidcClientKey));
        }

        if (row.OidcPreviousClientKey is { Length: > 0 })
        {
            keys.Add((row.OidcPreviousClientKeyId ?? "", row.OidcPreviousClientKey));
        }

        if (change.RetireClientKeyId is not null)
        {
            var index = keys.FindIndex(key => string.Equals(key.KeyId, change.RetireClientKeyId, StringComparison.Ordinal));
            if (index < 0)
            {
                return Refused("The tenant has no client key with that key id.");
            }

            keys.RemoveAt(index);
        }

        string? pkcs8Pem = null;
        string? addedKeyId = null;
        string? addedPublicKeyPem = null;
        if (change.ClientKey is not null)
        {
            // Problem has read this PEM already.
            TenantClientKeys.TryRead(change.ClientKey, out var pem, out var keyId, out var publicKeyPem);
            if (keys.Exists(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal)))
            {
                return Refused("The tenant already holds this client key.");
            }

            pkcs8Pem = pem;
            addedKeyId = keyId;
            addedPublicKeyPem = publicKeyPem;
            keys.Insert(0, (keyId, null));
        }

        if (keys.Count > 2)
        {
            return Refused("A tenant holds at most two client keys. Retire one first.");
        }

        if ((change.Profile ?? row.OidcProfile) == TenantSignInProfiles.Fapi2 && keys.Count == 0)
        {
            return Refused(change.Profile == TenantSignInProfiles.Fapi2
                ? $"The tenant '{tenantId}' has no client key, which the fapi2 profile needs."
                : "Retiring the last client key of a fapi2 tenant is refused.");
        }

        var claims = change.Claims.ToDictionary(claim => claim.Key, claim => claim.Value, StringComparer.Ordinal);
        var changed = new List<string>();
        AddIf(changed, "authority", change.Authority is not null);
        AddIf(changed, "clientId", change.ClientId is not null);
        AddIf(changed, "clientSecret", change.ClientSecret is not null);
        AddIf(changed, "claimUserId", claims.ContainsKey("UserId"));
        AddIf(changed, "claimDisplayName", claims.ContainsKey("DisplayName"));
        AddIf(changed, "claimRoles", claims.ContainsKey("Roles"));
        AddIf(changed, "claimEmail", claims.ContainsKey("Email"));
        AddIf(changed, "claimLocale", claims.ContainsKey("Locale"));
        AddIf(changed, "endpointAudience", change.EndpointAudience is not null);
        AddIf(changed, "serviceAudience", change.ServiceAudience is not null);
        AddIf(changed, "profile", change.Profile is not null);
        if (change.RetireClientKeyId is not null)
        {
            changed.Add($"retireClientKey:{change.RetireClientKeyId}");
        }

        if (addedKeyId is not null)
        {
            changed.Add($"clientKey:{addedKeyId}");
        }

        try
        {
            // A separate context, so that a failed record is never tracked again and the row is saved only after the record.
            await using var audit = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
            await PlatformAuditRecords.WriteAsync(audit, command, tenantId, changed, cancellationToken);
        }
        catch (Exception exception)
        {
            return new TenantSignInResult(TenantSignInOutcome.AuditFailed, null, exception, []);
        }

        if (change.Authority is not null)
        {
            row.OidcAuthority = change.Authority;
        }

        if (change.ClientId is not null)
        {
            row.OidcClientId = change.ClientId;
        }

        if (change.ClientSecret is not null)
        {
            row.OidcClientSecret = cipher.Encrypt(tenantId, TenantSecretFields.OidcClientSecret, change.ClientSecret);
        }

        if (claims.TryGetValue("UserId", out var userId))
        {
            row.ClaimUserId = NullIfEmpty(userId);
        }

        if (claims.TryGetValue("DisplayName", out var displayName))
        {
            row.ClaimDisplayName = NullIfEmpty(displayName);
        }

        if (claims.TryGetValue("Roles", out var roles))
        {
            row.ClaimRoles = NullIfEmpty(roles);
        }

        if (claims.TryGetValue("Email", out var email))
        {
            row.ClaimEmail = NullIfEmpty(email);
        }

        if (claims.TryGetValue("Locale", out var locale))
        {
            row.ClaimLocale = NullIfEmpty(locale);
        }

        if (change.EndpointAudience is not null)
        {
            row.EndpointAudience = NullIfEmpty(change.EndpointAudience);
        }

        if (change.ServiceAudience is not null)
        {
            row.ServiceAudience = NullIfEmpty(change.ServiceAudience);
        }

        if (change.Profile is not null)
        {
            row.OidcProfile = change.Profile;
        }

        if (change.ClientKey is not null || change.RetireClientKeyId is not null)
        {
            // Both columns hold the field oidc_client_key, so a stored key moves between them as it is.
            var stored = keys
                .Select(key => (key.KeyId, Encrypted: key.Encrypted ?? cipher.Encrypt(tenantId, TenantSecretFields.OidcClientKey, pkcs8Pem!)))
                .ToList();
            row.OidcClientKeyId = stored.Count > 0 ? stored[0].KeyId : null;
            row.OidcClientKey = stored.Count > 0 ? stored[0].Encrypted : null;
            row.OidcPreviousClientKeyId = stored.Count > 1 ? stored[1].KeyId : null;
            row.OidcPreviousClientKey = stored.Count > 1 ? stored[1].Encrypted : null;
        }

        row.Version++;
        await platform.SaveChangesAsync(cancellationToken);
        return new TenantSignInResult(TenantSignInOutcome.Changed, null, null, changed, addedKeyId, addedPublicKeyPem);
    }

    private static TenantSignInResult Refused(string message) => new(TenantSignInOutcome.Refused, message, null, []);

    private static void AddIf(List<string> names, string name, bool given)
    {
        if (given)
        {
            names.Add(name);
        }
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
