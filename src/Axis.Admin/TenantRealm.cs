using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Admin;

/// <summary>The identity provider's admin address and the client that <see cref="TenantRealm"/> signs in as, in the master realm.</summary>
internal sealed record IdentityProviderAdmin(Uri BaseAddress, string ClientId, string ClientSecret);

/// <summary>The secrets of the three clients of a tenant's realm.</summary>
internal sealed record TenantRealmSecrets(string Platform, string Activation, string Users);

/// <summary>
/// Creates a tenant's realm in Axis's identity provider through its admin API, or brings an existing one up to date.
/// The realm gets its display name, sessions of at most 8 hours and the account rules: an authenticator code at every
/// sign-in, passwords of 12 to 128 characters that are not on the breached-password list, a wait after 5 failed sign-ins,
/// a password reset link that lasts 30 minutes and no self-registration. It also gets three clients: the platform client
/// <c>axis</c>, <c>axis-activation</c> with rights over the realm's providers and clients, and <c>axis-users</c> with
/// rights over its users only. The identity provider must hold the breached-password list file. A second run updates
/// what the first made. An error names the step and the status code,
/// never a response body or a secret.
/// </summary>
internal sealed class TenantRealm(HttpClient http, IdentityProviderAdmin admin)
{
    public const int SessionMaxLifespanSeconds = 28800;
    public const string PlatformClientId = "axis";
    public const string ActivationClientId = "axis-activation";
    public const string UsersClientId = "axis-users";

    /// <summary>The file in the identity provider's <c>data/password-blacklists</c> folder that the password policy names.</summary>
    public const string BreachedPasswordList = "axis-breached-passwords.txt";

    public const string PasswordPolicy = $"length(12) and maxLength(128) and passwordBlacklist({BreachedPasswordList})";
    public const int PasswordResetLifespanSeconds = 1800;

    private const string ManagementClientId = "realm-management";

    private static readonly string[] _activationRoles =
    [
        "manage-identity-providers", "view-identity-providers", "manage-clients", "view-clients", "query-clients",
    ];

    private static readonly string[] _usersRoles = ["manage-users", "view-users", "query-users", "query-groups"];

    public async Task EnsureAsync(string realm, string displayName, TenantRealmSecrets secrets, CancellationToken cancellationToken)
    {
        var token = await AdminTokenAsync(cancellationToken);
        var realmPath = $"admin/realms/{Uri.EscapeDataString(realm)}";

        await EnsureRealmAsync(token, realm, realmPath, displayName, cancellationToken);
        await EnsureSecondStepAsync(token, realmPath, cancellationToken);
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
            ["ssoSessionMaxLifespan"] = SessionMaxLifespanSeconds,
            ["ssoSessionMaxLifespanRememberMe"] = SessionMaxLifespanSeconds,
            ["registrationAllowed"] = false,
            ["resetPasswordAllowed"] = true,
            ["actionTokenGeneratedByUserLifespan"] = PasswordResetLifespanSeconds,
            ["passwordPolicy"] = PasswordPolicy,
            // Keycloak's temporary lockout: 1 minute after 5 failures in a row, 1 minute more with each further 5, up to 15.
            // The quick-login check would lock an account after 2 failures within a second, so it is off.
            ["bruteForceProtected"] = true,
            ["permanentLockout"] = false,
            ["maxTemporaryLockouts"] = 0,
            ["failureFactor"] = 5,
            ["waitIncrementSeconds"] = 60,
            ["maxFailureWaitSeconds"] = 900,
            ["quickLoginCheckMilliSeconds"] = 0,
            ["otpPolicyType"] = "totp",
            ["otpPolicyDigits"] = 6,
            ["otpPolicyPeriod"] = 30,
            ["otpPolicyAlgorithm"] = "HmacSHA1",
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
