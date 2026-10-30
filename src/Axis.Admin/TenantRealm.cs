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
/// users, <see cref="RealmSync.FirstLoginFlowAlias"/>, whose only step creates a new user, and it turns off the Verify Profile step. It also gets three clients: the platform client
/// <c>axis</c>, <c>axis-activation</c> with rights over the realm's providers and clients and over the realm's texts (<c>manage-realm</c>, which
/// Keycloak requires for them and which also covers every realm setting), and <c>axis-users</c> with
/// rights over its users only. The realm uses the Axis theme <see cref="LoginTheme"/> as its login theme. The identity provider
/// must hold the theme and the breached-password list file. A second run updates what the first made, and sets the theme again. An error names the step and the status code,
/// never a response body or a secret.
/// </summary>
internal sealed class TenantRealm(HttpClient http, IdentityProviderAdmin admin)
{
    public const int SessionMaxLifespanSeconds = 28800;
    public const string PlatformClientId = "axis";
    public const string ActivationClientId = "axis-activation";
    public const string UsersClientId = "axis-users";

    /// <summary>The Axis sign-in theme that the realm uses as its login theme. The identity provider holds it in its <c>themes</c> folder.</summary>
    public const string LoginTheme = "axis";

    /// <summary>The file in the identity provider's <c>data/password-blacklists</c> folder that the password policy names.</summary>
    public const string BreachedPasswordList = "axis-breached-passwords.txt";

    public const string PasswordPolicy = $"length(12) and maxLength(128) and passwordBlacklist({BreachedPasswordList})";
    public const int PasswordResetLifespanSeconds = 1800;

    private const string ManagementClientId = "realm-management";
    private const string CreateUserIfUnique = "idp-create-user-if-unique";

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
    public async Task EnsureForTenantAsync(PlatformDbContext platform, TenantRow row, TenantSecretCipher cipher, CancellationToken cancellationToken)
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

        // The display name is the tenant id until the command takes the organization's name.
        await EnsureAsync(id, id, secrets, cancellationToken);

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

    public async Task EnsureAsync(string realm, string displayName, TenantRealmSecrets secrets, CancellationToken cancellationToken)
    {
        var token = await AdminTokenAsync(cancellationToken);
        var realmPath = $"admin/realms/{Uri.EscapeDataString(realm)}";

        await EnsureRealmAsync(token, realm, realmPath, displayName, cancellationToken);
        await EnsureSecondStepAsync(token, realmPath, cancellationToken);
        await EnsureFirstLoginFlowAsync(token, realmPath, cancellationToken);
        await EnsureNoProfileReviewAsync(token, realmPath, cancellationToken);
        await EnsureClientAsync(token, realmPath, PlatformClient(secrets.Platform), cancellationToken);
        var activation = await EnsureClientAsync(token, realmPath, ServiceClient(ActivationClientId, secrets.Activation), cancellationToken);
        var users = await EnsureClientAsync(token, realmPath, ServiceClient(UsersClientId, secrets.Users), cancellationToken);

        var management = await ClientUuidAsync(token, realmPath, ManagementClientId, cancellationToken)
            ?? throw new InvalidOperationException($"The identity provider step 'find the {ManagementClientId} client' found no client.");
        await GrantRolesAsync(token, realmPath, management, activation, _activationRoles, cancellationToken);
        await GrantRolesAsync(token, realmPath, management, users, _usersRoles, cancellationToken);
    }

    private async Task<string> AdminTokenAsync(CancellationToken cancellationToken)
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

    private async Task EnsureRealmAsync(string token, string realm, string realmPath, string displayName, CancellationToken cancellationToken)
    {
        using var existing = await SendAsync(token, HttpMethod.Get, realmPath, null, cancellationToken);
        if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            using var created = await SendAsync(token, HttpMethod.Post, "admin/realms", RealmRepresentation(displayName, realm), cancellationToken);
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

        using var updated = await SendAsync(token, HttpMethod.Put, realmPath, RealmRepresentation(displayName, null), cancellationToken);
        EnsureSuccess(updated, "update the realm");
    }

    private static Dictionary<string, object?> RealmRepresentation(string displayName, string? realm)
    {
        var representation = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["displayName"] = displayName,
            ["loginTheme"] = LoginTheme,
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
        };
        if (realm is not null)
        {
            representation["realm"] = realm;
        }

        return representation;
    }

    /// <summary>
    /// Makes the OTP form of the realm's browser flow, and the subflow that holds it, required. The built-in flow asks for a
    /// code only from an account that already has an authenticator, so a new account would sign in with its password alone.
    /// </summary>
    private async Task EnsureSecondStepAsync(string token, string realmPath, CancellationToken cancellationToken)
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
    private async Task EnsureNoProfileReviewAsync(string token, string realmPath, CancellationToken cancellationToken)
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
    /// Makes the flow that brokered providers use at a person's first sign-in. Its one required step creates a new user if
    /// none has the username. It has no step that reviews the profile, offers to link an existing user or asks for an email.
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
        if (!executions.Any(execution => ProviderOf(execution) == CreateUserIfUnique))
        {
            using var added = await SendAsync(
                token, HttpMethod.Post, $"{path}/execution", new Dictionary<string, object?> { ["provider"] = CreateUserIfUnique }, cancellationToken);
            EnsureSuccess(added, "add the create-user step");
            executions = await ReadExecutionsAsync(token, path, cancellationToken);
        }

        var keptCreate = false;
        foreach (var execution in executions)
        {
            if (ProviderOf(execution) == CreateUserIfUnique && !keptCreate)
            {
                keptCreate = true;
                continue;
            }

            using var deleted = await SendAsync(
                token, HttpMethod.Delete, $"{realmPath}/authentication/executions/{execution.GetProperty("id").GetString()}", null, cancellationToken);
            EnsureSuccess(deleted, "remove another step of the first-login flow");
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

    private static Dictionary<string, object?> ServiceClient(string clientId, string secret) => new()
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

    private async Task<string?> ClientUuidAsync(string token, string realmPath, string clientId, CancellationToken cancellationToken)
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

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(admin.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string step)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The identity provider step '{step}' failed with status {(int)response.StatusCode}.");
        }
    }
}
