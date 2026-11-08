using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Configuration.Resources;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;

namespace Axis.Admin;

/// <summary>The identity provider's admin address and the client that <see cref="TenantRealm"/> signs in as, in the master realm.</summary>
internal sealed record IdentityProviderAdmin(Uri BaseAddress, string ClientId, string ClientSecret);

/// <summary>The secrets of the three clients of a tenant's realm.</summary>
internal sealed record TenantRealmSecrets(string Platform, string Activation, string Users);

/// <summary>
/// Creates a tenant's realm in Axis's identity provider through its admin API, or brings an existing one up to date.
/// The realm gets its display name, sessions of at most 8 hours and the account rules: an authenticator code at every
/// sign-in, passwords of 12 to 128 characters that are not on the breached-password list, a wait after 5 failed sign-ins,
/// a password reset link that lasts 30 minutes and no self-registration. It offers the platform locales
/// (<see cref="PlatformLocales"/>) on its sign-in pages. It allows two users to share an email, which
/// turns sign-in by email off, so an account's username is its email. It gets a first-login flow of its own for brokered
/// users, <see cref="RealmSync.FirstLoginFlowAlias"/>, whose steps are a deny step that <see cref="RealmUsersQuota"/> switches, off when made, and one that creates a new user, and it turns off the Verify Profile step. It also turns off the account console client and takes <c>manage-account</c> out of the
/// realm's default roles, as Axis's account page replaces Keycloak's. It also gets three clients: the platform client
/// <c>axis</c>, which <see cref="EnsureForTenantAsync"/> gives the redirect addresses of the tenant's hosts, <c>axis-activation</c> with rights over the realm's providers and clients and over the realm's texts (<c>manage-realm</c>, which
/// Keycloak requires for them and which also covers every realm setting), and <c>axis-users</c> with
/// rights over its users only. The realm uses the Axis theme <see cref="LoginTheme"/> as its login theme and <see cref="EmailTheme"/> as its email theme. The identity provider
/// must hold the themes and the breached-password list file. The realm's mail server (<c>smtpServer</c>) is the one that
/// <see cref="RealmMailServer"/> maps from the tenant's mail settings or the host's, so a password reset link is sent. A second run updates what the first made, and sets the themes, the client and the roles again. An error names the step and the status code,
/// never a response body or a secret. A server error (5xx) makes it read the realm again and repeat, up to three attempts.
/// It also gets the token rules of the MCP sign-in (see <c>docs/reference/mcp-server.md#sign-in</c>): access tokens of <see cref="AccessTokenLifespanSeconds"/>
/// seconds, refresh tokens that work once, and a client policy that refuses an authorization request from a public client that sends no PKCE <c>S256</c> challenge.
/// The client scope <see cref="RealmSync.AuthoringScope"/> comes from <see cref="RealmSync.SyncPlatformAddressesAsync"/>, which <see cref="EnsureForTenantAsync"/> calls.
/// It lets an outside MCP client register itself without a token, under the rules of <c>docs/reference/mcp-server.md#client-registration</c>: the client policy
/// <see cref="RegistrationPolicy"/> and the realm's anonymous registration policies.
/// <see cref="DeleteAsync"/> deletes a realm, and a realm that is already gone counts as deleted.
/// </summary>
internal sealed class TenantRealm(HttpClient http, IdentityProviderAdmin admin)
{
    public const int SessionMaxLifespanSeconds = 28800;
    public const string PlatformClientId = "axis";
    public const string ActivationClientId = "axis-activation";
    public const string UsersClientId = "axis-users";

    /// <summary>The Axis sign-in theme that the realm uses as its login theme. The identity provider holds it in its <c>themes</c> folder.</summary>
    public const string LoginTheme = "axis";

    /// <summary>The Axis email theme that the realm uses as its email theme. The identity provider holds it in its <c>themes</c> folder, beside the login theme.</summary>
    public const string EmailTheme = "axis";

    /// <summary>The file in the identity provider's <c>data/password-blacklists</c> folder that the password policy names.</summary>
    public const string BreachedPasswordList = "axis-breached-passwords.txt";

    public const string PasswordPolicy = $"length(12) and maxLength(128) and passwordBlacklist({BreachedPasswordList})";
    public const int PasswordResetLifespanSeconds = 1800;

    /// <summary>How long an access token of the realm lasts.</summary>
    public const int AccessTokenLifespanSeconds = 300;

    /// <summary>The name of the client policy and of its profile that require PKCE of every public client.</summary>
    public const string PublicPkcePolicy = "axis-public-pkce";

    /// <summary>The name of the client policy and of its profile that hold the rules of an anonymous client registration.</summary>
    public const string RegistrationPolicy = "axis-mcp-registration";

    private const string ManagementClientId = "realm-management";
    private const string AccountConsoleClientId = "account-console";
    private const string AccountClientId = "account";
    private const string ManageAccountRole = "manage-account";
    private const string CreateUserIfUnique = "idp-create-user-if-unique";
    private const int ServerErrorAttempts = 3;

    // An anonymous registration may name only these redirect addresses: any https address, and http only on the IPv4 or IPv6 loopback address (RFC 8252).
    private static readonly string[] _registrationRedirectPatterns =
    [
        @"^https://[^\s*]+$",
        @"^http://(127\.0\.0\.1|\[::1\])(:[0-9]{1,5})?(/[^\s*]*)?$",
    ];

    private static readonly string[] _activationRoles =
    [
        "manage-identity-providers", "view-identity-providers", "manage-clients", "view-clients", "query-clients",
        "manage-realm",
    ];

    private static readonly string[] _usersRoles = ["manage-users", "view-users", "query-users", "query-groups"];

    /// <summary>The address of the identity provider, ending with a slash.</summary>
    public Uri BaseAddress => admin.BaseAddress;

    /// <summary>
    /// Makes the realm of a main tenant, named after the tenant id, and points the tenant's sign-in settings at it. The three
    /// client secrets are generated, or kept when the row holds them already. They and the <c>Oidc</c> block are saved,
    /// encrypted, before the identity provider is called, so a stop between the two never loses a secret.
    /// </summary>
    public async Task EnsureForTenantAsync(PlatformDbContext platform, TenantRow row, TenantSecretCipher cipher, RealmMail mail, CancellationToken cancellationToken)
    {
        var id = row.Id;
        var changed = false;
        changed |= Ensure(row.OidcClientSecret, TenantSecretFields.OidcClientSecret, value => row.OidcClientSecret = value);
        changed |= Ensure(row.RealmActivationClientSecret, TenantSecretFields.RealmActivationClientSecret, value => row.RealmActivationClientSecret = value);
        changed |= Ensure(row.RealmUsersClientSecret, TenantSecretFields.RealmUsersClientSecret, value => row.RealmUsersClientSecret = value);

        var authority = new Uri(admin.BaseAddress, $"realms/{Uri.EscapeDataString(id)}").ToString();
        if (row.OidcAuthority != authority || row.OidcClientId != PlatformClientId)
        {
            row.OidcAuthority = authority;
            row.OidcClientId = PlatformClientId;
            changed = true;
        }

        if (changed)
        {
            row.Version++;
            await platform.SaveChangesAsync(cancellationToken);
        }

        var secrets = new TenantRealmSecrets(
            cipher.Decrypt(id, TenantSecretFields.OidcClientSecret, row.OidcClientSecret).Reveal(),
            cipher.Decrypt(id, TenantSecretFields.RealmActivationClientSecret, row.RealmActivationClientSecret!).Reveal(),
            cipher.Decrypt(id, TenantSecretFields.RealmUsersClientSecret, row.RealmUsersClientSecret!).Reveal());

        // The realm has no display name until the command takes the organization's name, so the pages name Axis, never the tenant id.
        await EnsureAsync(id, string.Empty, secrets, mail.For(DirectoryTenantCatalog.Email(row, cipher)), cancellationToken);

        // The platform client gets the addresses of the tenant's hosts, so a person can finish a sign-in on each of them.
        var hosts = await platform.TenantHosts.AsNoTracking().Where(host => host.TenantId == id).Select(host => host.Host).ToListAsync(cancellationToken);
        await new RealmSync(http, admin.BaseAddress).SyncPlatformAddressesAsync(id, secrets.Activation, hosts, cancellationToken);

        // A secret that is stored is kept, so a rerun never changes it.
        bool Ensure(byte[]? stored, string field, Action<byte[]> write)
        {
            if (stored is { Length: > 0 })
            {
                return false;
            }

            write(cipher.Encrypt(id, field, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))));
            return true;
        }
    }

    /// <summary>Whether the identity provider has the realm: 200 is true and 404 is false. Any other status throws.</summary>
    public async Task<bool> ExistsAsync(string realm, CancellationToken cancellationToken)
    {
        var token = await AdminTokenAsync(cancellationToken);
        using var response = await SendAsync(token, HttpMethod.Get, $"admin/realms/{Uri.EscapeDataString(realm)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        EnsureSuccess(response, "read the realm");
        return true;
    }

    /// <summary>Deletes the realm with its users and clients. A realm that does not exist (404) counts as deleted. Any other failure throws.</summary>
    public async Task DeleteAsync(string realm, CancellationToken cancellationToken)
    {
        var token = await AdminTokenAsync(cancellationToken);
        using var response = await SendAsync(token, HttpMethod.Delete, $"admin/realms/{Uri.EscapeDataString(realm)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        EnsureSuccess(response, "delete the realm");
    }

    public async Task EnsureAsync(
        string realm, string displayName, TenantRealmSecrets secrets, IReadOnlyDictionary<string, string> smtpServer, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await EnsureOnceAsync(realm, displayName, secrets, smtpServer, cancellationToken);
                return;
            }
            catch (ServerErrorException) when (attempt < ServerErrorAttempts)
            {
                // The identity provider failed for a moment. Read the realm again, so a step that did land turns into
                // an update, and wait a little longer each time with some jitter.
                await Task.Delay(TimeSpan.FromMilliseconds((200 * attempt) + Random.Shared.Next(0, 200)), cancellationToken);
            }
        }
    }

    private async Task EnsureOnceAsync(
        string realm, string displayName, TenantRealmSecrets secrets, IReadOnlyDictionary<string, string> smtpServer, CancellationToken cancellationToken)
    {
        var token = await AdminTokenAsync(cancellationToken);
        var realmPath = $"admin/realms/{Uri.EscapeDataString(realm)}";

        await EnsureRealmAsync(token, realm, realmPath, displayName, smtpServer, cancellationToken);
        await EnsureAuthoringAsync(token, realmPath, cancellationToken);
        await EnsureRegistrationAsync(token, realmPath, cancellationToken);
        await EnsureSecondStepAsync(token, realmPath, cancellationToken);
        await EnsureFirstLoginFlowAsync(token, realmPath, cancellationToken);
        await EnsureNoProfileReviewAsync(token, realmPath, cancellationToken);
        await EnsureNoAccountConsoleAsync(token, realmPath, cancellationToken);
        await EnsureClientAsync(token, realmPath, PlatformClient(secrets.Platform), cancellationToken);
        var activation = await EnsureClientAsync(token, realmPath, ServiceClient(ActivationClientId, secrets.Activation), cancellationToken);
        var users = await EnsureClientAsync(token, realmPath, ServiceClient(UsersClientId, secrets.Users), cancellationToken);

        var management = await ClientUuidAsync(token, realmPath, ManagementClientId, cancellationToken)
            ?? throw new InvalidOperationException($"The identity provider step 'find the {ManagementClientId} client' found no client.");
        await GrantRolesAsync(token, realmPath, management, activation, _activationRoles, cancellationToken);
        await GrantRolesAsync(token, realmPath, management, users, _usersRoles, cancellationToken);
    }

    internal async Task<string> AdminTokenAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = admin.ClientId,
            ["client_secret"] = admin.ClientSecret,
        });
        using var response = await http.PostAsync(new Uri(admin.BaseAddress, "realms/master/protocol/openid-connect/token"), content, cancellationToken);
        EnsureSuccess(response, "get the admin token");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    internal async Task EnsureRealmAsync(
        string token, string realm, string realmPath, string displayName, IReadOnlyDictionary<string, string> smtpServer, CancellationToken cancellationToken)
    {
        using var existing = await SendAsync(token, HttpMethod.Get, realmPath, null, cancellationToken);
        if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            using var created = await SendAsync(token, HttpMethod.Post, "admin/realms", RealmRepresentation(displayName, realm, smtpServer), cancellationToken);
            // A concurrent create made the realm first, so the update below brings it in step.
            if (created.StatusCode != HttpStatusCode.Conflict)
            {
                EnsureSuccess(created, "create the realm");
                return;
            }
        }
        else
        {
            EnsureSuccess(existing, "read the realm");
        }

        using var updated = await SendAsync(token, HttpMethod.Put, realmPath, RealmRepresentation(displayName, null, smtpServer), cancellationToken);
        EnsureSuccess(updated, "update the realm");
    }

    private static Dictionary<string, object?> RealmRepresentation(string displayName, string? realm, IReadOnlyDictionary<string, string> smtpServer)
    {
        var representation = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["displayName"] = displayName,
            ["loginTheme"] = LoginTheme,
            ["emailTheme"] = EmailTheme,
            ["ssoSessionMaxLifespan"] = SessionMaxLifespanSeconds,
            ["ssoSessionMaxLifespanRememberMe"] = SessionMaxLifespanSeconds,
            ["registrationAllowed"] = false,
            // Keycloak cannot allow duplicate emails together with sign-in by email, so an account's username is its email.
            ["duplicateEmailsAllowed"] = true,
            ["loginWithEmailAllowed"] = false,
            ["registrationEmailAsUsername"] = false,
            ["resetPasswordAllowed"] = true,
            ["actionTokenGeneratedByUserLifespan"] = PasswordResetLifespanSeconds,
            ["passwordPolicy"] = PasswordPolicy,
            // Keycloak's temporary lockout: 1 minute after 5 failures in a row, 1 minute more with each further 5, up to 15.
            // The quick-login check would lock an account after 2 failures within a second, so it is off and its wait is 0,
            // which means that a hit of it never locks an account before 5 failures.
            ["bruteForceProtected"] = true,
            ["permanentLockout"] = false,
            ["maxTemporaryLockouts"] = 0,
            ["failureFactor"] = 5,
            ["waitIncrementSeconds"] = 60,
            ["maxFailureWaitSeconds"] = 900,
            ["quickLoginCheckMilliSeconds"] = 0,
            ["minimumQuickLoginWaitSeconds"] = 0,
            ["otpPolicyType"] = "totp",
            ["otpPolicyDigits"] = 6,
            ["otpPolicyPeriod"] = 30,
            ["otpPolicyAlgorithm"] = "HmacSHA1",
            ["internationalizationEnabled"] = true,
            ["supportedLocales"] = PlatformLocales.All,
            ["defaultLocale"] = PlatformLocales.Fallback,
            // An empty map clears the mail server. The map can hold the SMTP password, so nothing logs the representation.
            ["smtpServer"] = smtpServer,
        };
        if (realm is not null)
        {
            representation["realm"] = realm;
        }

        return representation;
    }

    /// <summary>
    /// Sets the token rules of the MCP sign-in: access tokens of <see cref="AccessTokenLifespanSeconds"/> seconds, and refresh tokens that
    /// rotate and cannot be used again (<c>refreshTokenMaxReuse</c> 0). It also gives the realm the client policy <see cref="PublicPkcePolicy"/>, with the
    /// <c>pkce-enforcer</c> executor for every client with the <c>public</c> access type. The executor adds the PKCE attribute to a public client that
    /// lacks it, because Keycloak would refuse to create or update such a client otherwise. It replaces the profile and the policy of that name,
    /// keeps every other realm profile and policy, and never writes Keycloak's global profiles.
    /// </summary>
    internal async Task EnsureAuthoringAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var settings = new Dictionary<string, object?>
        {
            ["accessTokenLifespan"] = AccessTokenLifespanSeconds,
            ["revokeRefreshToken"] = true,
            ["refreshTokenMaxReuse"] = 0,
        };
        using var updated = await SendAsync(token, HttpMethod.Put, realmPath, settings, cancellationToken);
        EnsureSuccess(updated, "set the token rules");

        var profile = new Dictionary<string, object?>
        {
            ["name"] = PublicPkcePolicy,
            ["description"] = "Requires PKCE S256 of every public client.",
            ["executors"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["executor"] = "pkce-enforcer",
                    ["configuration"] = new Dictionary<string, object?> { ["auto-configure"] = true },
                },
            },
        };
        await ReplaceClientPolicyEntryAsync(token, $"{realmPath}/client-policies/profiles", "profiles", "profile", profile, cancellationToken);

        var policy = new Dictionary<string, object?>
        {
            ["name"] = PublicPkcePolicy,
            ["description"] = "Applies the PKCE profile to every public client.",
            ["enabled"] = true,
            ["conditions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["condition"] = "client-access-type",
                    ["configuration"] = new Dictionary<string, object?> { ["type"] = new[] { "public" } },
                },
            },
            ["profiles"] = new[] { PublicPkcePolicy },
        };
        await ReplaceClientPolicyEntryAsync(token, $"{realmPath}/client-policies/policies", "policies", "policy", policy, cancellationToken);
    }

    /// <summary>
    /// Lets an outside MCP client register itself at the realm without a token, as a public client that requires consent
    /// (<c>docs/reference/mcp-server.md#client-registration</c>). The client policy <see cref="RegistrationPolicy"/> applies to a client that an anonymous
    /// registration writes and to an update with the registration access token that the registration returns, and never to a client that an administrator or Axis writes. Its executors refuse a redirect address that is neither <c>https</c>
    /// nor <c>http</c> on a loopback address, the implicit flow, the password flow and every client authenticator, which makes a confidential client impossible,
    /// and they set consent required and the full scope off. The realm's anonymous registration
    /// policies lose the Trusted Hosts policy, which Keycloak cannot loosen, and a Consent Required policy is added when it is missing. The Allowed Client Scopes
    /// policy comes from <see cref="RealmSync.SyncPlatformAddressesAsync"/>, because Keycloak refuses <see cref="RealmSync.AuthoringScope"/> before the scope exists.
    /// Every other registration policy stays as it is.
    /// </summary>
    internal async Task EnsureRegistrationAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var registration = new Dictionary<string, object?>
        {
            ["name"] = RegistrationPolicy,
            ["description"] = "Holds the rules of an anonymous client registration.",
            ["executors"] = new object[]
            {
                // Keycloak accepts an http loopback address only when it accepts every http address, so the pattern executor narrows it.
                new Dictionary<string, object?>
                {
                    ["executor"] = "secure-redirect-uris-enforcer",
                    ["configuration"] = new Dictionary<string, object?>
                    {
                        ["allow-ipv4-loopback-address"] = true,
                        ["allow-ipv6-loopback-address"] = true,
                        ["allow-http-scheme"] = true,
                        ["allow-private-use-uri-scheme"] = false,
                        ["allow-wildcard-context-path"] = false,
                    },
                },
                new Dictionary<string, object?>
                {
                    ["executor"] = "secure-client-uris-pattern",
                    ["configuration"] = new Dictionary<string, object?>
                    {
                        ["client-uri-fields"] = new[] { "redirectUris" },
                        ["allowed-patterns"] = _registrationRedirectPatterns,
                    },
                },
                Executor("reject-implicit-grant", new Dictionary<string, object?> { ["auto-configure"] = false }),
                Executor("reject-ropc-grant", new Dictionary<string, object?> { ["auto-configure"] = false }),
                Executor("consent-required", new Dictionary<string, object?> { ["auto-configure"] = true }),
                Executor("full-scope-disabled", new Dictionary<string, object?> { ["auto-configure"] = true }),
                // A public client has no authenticator, so an empty list leaves it alone and refuses a client that has one.
                Executor("secure-client-authenticator", new Dictionary<string, object?> { ["allowed-client-authenticators"] = Array.Empty<string>() }),
            },
        };
        await ReplaceClientPolicyEntryAsync(token, $"{realmPath}/client-policies/profiles", "profiles", "profile", registration, cancellationToken);

        var registrationPolicy = new Dictionary<string, object?>
        {
            ["name"] = RegistrationPolicy,
            ["description"] = "Applies the registration rules to every client that an anonymous registration writes and that its registration access token updates.",
            ["enabled"] = true,
            ["conditions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["condition"] = "client-updater-context",
                    ["configuration"] = new Dictionary<string, object?> { ["update-client-source"] = new[] { "ByAnonymous", "ByRegistrationAccessToken" } },
                },
            },
            ["profiles"] = new[] { RegistrationPolicy },
        };
        await ReplaceClientPolicyEntryAsync(token, $"{realmPath}/client-policies/policies", "policies", "policy", registrationPolicy, cancellationToken);

        await EnsureAnonymousRegistrationPoliciesAsync(token, realmPath, cancellationToken);

        static Dictionary<string, object?> Executor(string name, Dictionary<string, object?> configuration) =>
            new() { ["executor"] = name, ["configuration"] = configuration };
    }

    private async Task EnsureAnonymousRegistrationPoliciesAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var componentsPath = $"{realmPath}/components";
        using var listed = await SendAsync(token, HttpMethod.Get, $"{componentsPath}?type={Uri.EscapeDataString(RealmSync.RegistrationPolicyType)}", null, cancellationToken);
        EnsureSuccess(listed, "read the registration policies");
        var anonymous = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray()
            .Where(component => component.TryGetProperty("subType", out var subType) && subType.GetString() == "anonymous").ToList();

        foreach (var trustedHosts in anonymous.Where(component => component.GetProperty("providerId").GetString() == "trusted-hosts"))
        {
            using var removed = await SendAsync(token, HttpMethod.Delete, $"{componentsPath}/{trustedHosts.GetProperty("id").GetString()}", null, cancellationToken);
            EnsureSuccess(removed, "remove the trusted hosts registration policy");
        }

        if (anonymous.Any(component => component.GetProperty("providerId").GetString() == "consent-required"))
        {
            return;
        }

        using var realm = await SendAsync(token, HttpMethod.Get, realmPath, null, cancellationToken);
        EnsureSuccess(realm, "read the realm");
        var consent = new Dictionary<string, object?>
        {
            ["name"] = "Consent Required",
            ["providerId"] = "consent-required",
            ["providerType"] = RealmSync.RegistrationPolicyType,
            ["parentId"] = (await realm.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetString(),
            ["subType"] = "anonymous",
            ["config"] = new Dictionary<string, string[]>(),
        };
        using var created = await SendAsync(token, HttpMethod.Post, componentsPath, consent, cancellationToken);
        EnsureSuccess(created, "add the consent required registration policy");
    }

    /// <summary>Reads the realm's list of client profiles or policies, replaces the entry named like <paramref name="entry"/> or adds it, and writes the list back.</summary>
    private async Task ReplaceClientPolicyEntryAsync(
        string token, string path, string property, string what, Dictionary<string, object?> entry, CancellationToken cancellationToken)
    {
        using var read = await SendAsync(token, HttpMethod.Get, path, null, cancellationToken);
        EnsureSuccess(read, $"read the client {what}s");
        var entries = new List<object?>();
        var body = await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (body.TryGetProperty(property, out var current) && current.ValueKind == JsonValueKind.Array)
        {
            entries.AddRange(current.EnumerateArray().Where(item => item.GetProperty("name").GetString() != (string)entry["name"]!).Cast<object?>());
        }

        entries.Add(entry);
        using var written = await SendAsync(token, HttpMethod.Put, path, new Dictionary<string, object?> { [property] = entries }, cancellationToken);
        EnsureSuccess(written, $"write the client {what}s");
    }

    /// <summary>
    /// Makes the OTP form of the realm's browser flow, and the subflow that holds it, required. The built-in flow asks for a
    /// code only from an account that already has an authenticator, so a new account would sign in with its password alone.
    /// </summary>
    internal async Task EnsureSecondStepAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var path = $"{realmPath}/authentication/flows/browser/executions";
        using var response = await SendAsync(token, HttpMethod.Get, path, null, cancellationToken);
        EnsureSuccess(response, "read the browser flow");
        var executions = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();

        var otp = executions.FindIndex(execution =>
            execution.TryGetProperty("providerId", out var provider) && provider.GetString() == "auth-otp-form");
        if (otp < 0)
        {
            throw new InvalidOperationException("The identity provider step 'find the OTP form' found no execution.");
        }

        var parent = executions.FindLastIndex(otp, execution =>
            execution.TryGetProperty("authenticationFlow", out var isFlow) && isFlow.ValueKind == JsonValueKind.True
            && execution.GetProperty("level").GetInt32() == executions[otp].GetProperty("level").GetInt32() - 1);
        foreach (var index in parent < 0 ? new[] { otp } : new[] { parent, otp })
        {
            var execution = executions[index];
            if (execution.GetProperty("requirement").GetString() == "REQUIRED")
            {
                continue;
            }

            var body = execution.Deserialize<Dictionary<string, JsonElement>>()!;
            body["requirement"] = JsonSerializer.SerializeToElement("REQUIRED");
            using var updated = await SendAsync(token, HttpMethod.Put, path, body, cancellationToken);
            EnsureSuccess(updated, "require the second step");
        }
    }

    /// <summary>
    /// Turns off Keycloak's Verify Profile required action, so no sign-in stops on a page that asks for a missing profile field,
    /// such as the last name that a <c>saml</c> provider may not send. An identity provider without the action has nothing to turn off.
    /// </summary>
    internal async Task EnsureNoProfileReviewAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var path = $"{realmPath}/authentication/required-actions/VERIFY_PROFILE";
        using var response = await SendAsync(token, HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        EnsureSuccess(response, "read the verify-profile action");
        var action = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).Deserialize<Dictionary<string, JsonElement>>()!;
        if (!action.TryGetValue("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
        {
            return;
        }

        action["enabled"] = JsonSerializer.SerializeToElement(false);
        using var updated = await SendAsync(token, HttpMethod.Put, path, action, cancellationToken);
        EnsureSuccess(updated, "turn off the verify-profile action");
    }

    /// <summary>
    /// Turns off Keycloak's account console, and takes <c>manage-account</c> out of the realm's default roles, so Axis's account
    /// page is the only self-service page. The older <c>account</c> client and the <c>view-profile</c> role stay, as Keycloak's
    /// application-initiated actions may need them. A realm without the console, or whose default roles lack the role, has nothing to change.
    /// </summary>
    internal async Task EnsureNoAccountConsoleAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var console = await ClientUuidAsync(token, realmPath, AccountConsoleClientId, cancellationToken);
        if (console is not null)
        {
            var path = $"{realmPath}/clients/{console}";
            using var response = await SendAsync(token, HttpMethod.Get, path, null, cancellationToken);
            EnsureSuccess(response, "read the account console");
            var client = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).Deserialize<Dictionary<string, JsonElement>>()!;
            if (!client.TryGetValue("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False)
            {
                client["enabled"] = JsonSerializer.SerializeToElement(false);
                using var updated = await SendAsync(token, HttpMethod.Put, path, client, cancellationToken);
                EnsureSuccess(updated, "turn off the account console");
            }
        }

        var account = await ClientUuidAsync(token, realmPath, AccountClientId, cancellationToken);
        using var realm = await SendAsync(token, HttpMethod.Get, realmPath, null, cancellationToken);
        EnsureSuccess(realm, "read the realm");
        var defaultRole = (await realm.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).TryGetProperty("defaultRole", out var role) ? role : (JsonElement?)null;
        if (account is null || defaultRole is null)
        {
            return;
        }

        var composites = $"{realmPath}/roles-by-id/{Uri.EscapeDataString(defaultRole.Value.GetProperty("id").GetString()!)}/composites";
        using var held = await SendAsync(token, HttpMethod.Get, $"{composites}/clients/{account}", null, cancellationToken);
        EnsureSuccess(held, "read the default roles");
        var manage = (await held.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray()
            .Where(composite => composite.GetProperty("name").GetString() == ManageAccountRole).ToList();
        if (manage.Count > 0)
        {
            using var removed = await SendAsync(token, HttpMethod.Delete, composites, manage, cancellationToken);
            EnsureSuccess(removed, "remove manage-account from the default roles");
        }
    }

    /// <summary>
    /// Makes the flow that brokered providers use at a person's first sign-in. Its first step denies access with the message of
    /// <see cref="RealmUsersQuota.RefusalMessageKey"/>. It is off when made, a rerun leaves it as it is, and <see cref="RealmUsersQuota"/> switches it.
    /// Its second step is required and creates a new user if none has the username. It has no step that reviews the profile,
    /// offers to link an existing user or asks for an email.
    /// </summary>
    private async Task EnsureFirstLoginFlowAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        using var listed = await SendAsync(token, HttpMethod.Get, $"{realmPath}/authentication/flows", null, cancellationToken);
        EnsureSuccess(listed, "list the authentication flows");
        var exists = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray()
            .Any(flow => flow.GetProperty("alias").GetString() == RealmSync.FirstLoginFlowAlias);
        if (!exists)
        {
            var flow = new Dictionary<string, object?>
            {
                ["alias"] = RealmSync.FirstLoginFlowAlias,
                ["description"] = "Creates a new user at a person's first sign-in through a brokered provider and never links one.",
                ["providerId"] = "basic-flow",
                ["topLevel"] = true,
                ["builtIn"] = false,
            };
            using var created = await SendAsync(token, HttpMethod.Post, $"{realmPath}/authentication/flows", flow, cancellationToken);
            EnsureSuccess(created, "create the first-login flow");
        }

        var path = $"{realmPath}/authentication/flows/{Uri.EscapeDataString(RealmSync.FirstLoginFlowAlias)}/executions";
        var executions = await ReadExecutionsAsync(token, path, cancellationToken);
        if (!executions.Any(execution => ProviderOf(execution) == RealmUsersQuota.DenyAccessProvider))
        {
            // Keycloak adds a new step as DISABLED, which is how the quota step starts. A rerun never sets its requirement.
            using var added = await SendAsync(
                token, HttpMethod.Post, $"{path}/execution", new Dictionary<string, object?> { ["provider"] = RealmUsersQuota.DenyAccessProvider }, cancellationToken);
            EnsureSuccess(added, "add the users quota step");
            executions = await ReadExecutionsAsync(token, path, cancellationToken);
        }

        if (!executions.Any(execution => ProviderOf(execution) == CreateUserIfUnique))
        {
            using var added = await SendAsync(
                token, HttpMethod.Post, $"{path}/execution", new Dictionary<string, object?> { ["provider"] = CreateUserIfUnique }, cancellationToken);
            EnsureSuccess(added, "add the create-user step");
            executions = await ReadExecutionsAsync(token, path, cancellationToken);
        }

        var keptDeny = false;
        var keptCreate = false;
        var deletedAny = false;
        foreach (var execution in executions)
        {
            if (ProviderOf(execution) == RealmUsersQuota.DenyAccessProvider && !keptDeny)
            {
                keptDeny = true;
                continue;
            }

            if (ProviderOf(execution) == CreateUserIfUnique && !keptCreate)
            {
                keptCreate = true;
                continue;
            }

            using var deleted = await SendAsync(
                token, HttpMethod.Delete, $"{realmPath}/authentication/executions/{execution.GetProperty("id").GetString()}", null, cancellationToken);
            EnsureSuccess(deleted, "remove another step of the first-login flow");
            deletedAny = true;
        }

        if (deletedAny)
        {
            executions = await ReadExecutionsAsync(token, path, cancellationToken);
        }

        var denyIndex = executions.FindIndex(execution => ProviderOf(execution) == RealmUsersQuota.DenyAccessProvider);
        var createIndex = executions.FindIndex(execution => ProviderOf(execution) == CreateUserIfUnique);
        var deny = executions[denyIndex];
        if (denyIndex > createIndex)
        {
            using var raised = await SendAsync(
                token, HttpMethod.Post, $"{realmPath}/authentication/executions/{deny.GetProperty("id").GetString()}/raise-priority", null, cancellationToken);
            EnsureSuccess(raised, "move the users quota step first");
            executions = await ReadExecutionsAsync(token, path, cancellationToken);
            deny = executions.First(execution => ProviderOf(execution) == RealmUsersQuota.DenyAccessProvider);
        }

        if (!deny.TryGetProperty("authenticationConfig", out _))
        {
            var config = new Dictionary<string, object?>
            {
                ["alias"] = "axis users quota",
                ["config"] = new Dictionary<string, string> { [RealmUsersQuota.DenyMessageConfigKey] = RealmUsersQuota.RefusalMessageKey },
            };
            using var configured = await SendAsync(
                token, HttpMethod.Post, $"{realmPath}/authentication/executions/{deny.GetProperty("id").GetString()}/config", config, cancellationToken);
            EnsureSuccess(configured, "set the users quota message");
        }

        var create = executions.First(execution => ProviderOf(execution) == CreateUserIfUnique);
        if (create.GetProperty("requirement").GetString() != "REQUIRED")
        {
            var body = create.Deserialize<Dictionary<string, JsonElement>>()!;
            body["requirement"] = JsonSerializer.SerializeToElement("REQUIRED");
            using var updated = await SendAsync(token, HttpMethod.Put, path, body, cancellationToken);
            EnsureSuccess(updated, "require the create-user step");
        }
    }

    private async Task<List<JsonElement>> ReadExecutionsAsync(string token, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(token, HttpMethod.Get, path, null, cancellationToken);
        EnsureSuccess(response, "read the first-login flow");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();
    }

    private static string? ProviderOf(JsonElement execution) =>
        execution.TryGetProperty("providerId", out var provider) ? provider.GetString() : null;

    private static Dictionary<string, object?> PlatformClient(string secret) => new()
    {
        ["clientId"] = PlatformClientId,
        ["enabled"] = true,
        ["protocol"] = "openid-connect",
        ["publicClient"] = false,
        ["clientAuthenticatorType"] = "client-secret",
        ["secret"] = secret,
        ["standardFlowEnabled"] = true,
        ["directAccessGrantsEnabled"] = false,
        ["implicitFlowEnabled"] = false,
        ["serviceAccountsEnabled"] = false,
    };

    internal static Dictionary<string, object?> ServiceClient(string clientId, string secret) => new()
    {
        ["clientId"] = clientId,
        ["enabled"] = true,
        ["protocol"] = "openid-connect",
        ["publicClient"] = false,
        ["clientAuthenticatorType"] = "client-secret",
        ["secret"] = secret,
        ["standardFlowEnabled"] = false,
        ["directAccessGrantsEnabled"] = false,
        ["implicitFlowEnabled"] = false,
        ["serviceAccountsEnabled"] = true,
    };

    /// <summary>Creates the client, or updates the one that has its client id, and returns its internal id.</summary>
    private async Task<string> EnsureClientAsync(string token, string realmPath, Dictionary<string, object?> client, CancellationToken cancellationToken)
    {
        var clientId = (string)client["clientId"]!;
        var uuid = await ClientUuidAsync(token, realmPath, clientId, cancellationToken);
        if (uuid is null)
        {
            using var created = await SendAsync(token, HttpMethod.Post, $"{realmPath}/clients", client, cancellationToken);
            EnsureSuccess(created, $"create the client '{clientId}'");
            uuid = await ClientUuidAsync(token, realmPath, clientId, cancellationToken)
                ?? throw new InvalidOperationException($"The identity provider step 'find the created client '{clientId}'' found no client.");
            return uuid;
        }

        using var updated = await SendAsync(token, HttpMethod.Put, $"{realmPath}/clients/{uuid}", client, cancellationToken);
        EnsureSuccess(updated, $"update the client '{clientId}'");
        return uuid;
    }

    internal async Task<string?> ClientUuidAsync(string token, string realmPath, string clientId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(token, HttpMethod.Get, $"{realmPath}/clients?clientId={Uri.EscapeDataString(clientId)}", null, cancellationToken);
        EnsureSuccess(response, $"find the client '{clientId}'");
        var clients = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        foreach (var client in clients.EnumerateArray())
        {
            if (client.GetProperty("clientId").GetString() == clientId)
            {
                return client.GetProperty("id").GetString();
            }
        }

        return null;
    }

    /// <summary>Gives the client's service account the built-in admin roles. A role it already has stays as it is.</summary>
    private async Task GrantRolesAsync(
        string token, string realmPath, string managementUuid, string clientUuid, string[] roles, CancellationToken cancellationToken)
    {
        using var userResponse = await SendAsync(token, HttpMethod.Get, $"{realmPath}/clients/{clientUuid}/service-account-user", null, cancellationToken);
        EnsureSuccess(userResponse, "find the service account user");
        var user = await userResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var userId = user.GetProperty("id").GetString();

        var representations = new List<JsonElement>();
        foreach (var role in roles)
        {
            using var roleResponse = await SendAsync(
                token, HttpMethod.Get, $"{realmPath}/clients/{managementUuid}/roles/{Uri.EscapeDataString(role)}", null, cancellationToken);
            EnsureSuccess(roleResponse, $"read the role '{role}'");
            representations.Add(await roleResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
        }

        using var granted = await SendAsync(
            token, HttpMethod.Post, $"{realmPath}/users/{userId}/role-mappings/clients/{managementUuid}", representations, cancellationToken);
        EnsureSuccess(granted, "grant the admin roles");
    }

    internal async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(admin.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    internal static void EnsureSuccess(HttpResponseMessage response, string step)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = $"The identity provider step '{step}' failed with status {(int)response.StatusCode}.";
            throw (int)response.StatusCode >= 500 ? new ServerErrorException(message) : new InvalidOperationException(message);
        }
    }

    /// <summary>The identity provider answered a server error (5xx), which <see cref="EnsureAsync"/> may repeat.</summary>
    private sealed class ServerErrorException(string message) : InvalidOperationException(message);
}
