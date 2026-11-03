using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Tenancy.Realms;

namespace Axis.Admin;

/// <summary>
/// Creates a cell's operator realm in Axis's identity provider through its admin API, or brings an existing one up to date, and invites
/// a platform operator to it. The realm has the account rules of a tenant's realm, which <see cref="TenantRealm"/> sets: an authenticator
/// code at every sign-in, the password policy, the lockout, the Axis login theme and the mail server. It has no brokered identity
/// provider, so a provider that a run finds is removed, and it has no client, because the cell console's sign-in client comes later. It has
/// one realm role, the operator role. The invited account gets the actions to set a password and an authenticator, and the role. Every
/// call signs in as the admin client of the master realm. An error names the step and the status code, never a response body or a secret.
/// </summary>
internal sealed class OperatorRealm(HttpClient http, IdentityProviderAdmin admin)
{
    private static readonly string[] _invitationActions = ["UPDATE_PASSWORD", "CONFIGURE_TOTP"];

    private readonly TenantRealm _realm = new(http, admin);

    /// <summary>Makes the realm and its operator role, or brings them up to date, and removes every brokered identity provider.</summary>
    public async Task EnsureRealmAsync(string realm, string role, IReadOnlyDictionary<string, string> smtpServer, CancellationToken cancellationToken)
    {
        var token = await _realm.AdminTokenAsync(cancellationToken);
        var path = RealmPath(realm);

        await _realm.EnsureRealmAsync(token, realm, path, string.Empty, smtpServer, cancellationToken);
        await _realm.EnsureSecondStepAsync(token, path, cancellationToken);
        await _realm.EnsureNoProfileReviewAsync(token, path, cancellationToken);

        using var listed = await _realm.SendAsync(token, HttpMethod.Get, $"{path}/identity-provider/instances", null, cancellationToken);
        TenantRealm.EnsureSuccess(listed, "list the identity providers");
        var providers = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();
        foreach (var provider in providers)
        {
            var alias = provider.GetProperty("alias").GetString()!;
            using var removed = await _realm.SendAsync(
                token, HttpMethod.Delete, $"{path}/identity-provider/instances/{Uri.EscapeDataString(alias)}", null, cancellationToken);
            TenantRealm.EnsureSuccess(removed, "remove an identity provider");
        }

        if (await ReadRoleAsync(token, path, role, cancellationToken) is null)
        {
            var body = new Dictionary<string, object?> { ["name"] = role, ["description"] = "Signs in to the cell console." };
            using var created = await _realm.SendAsync(token, HttpMethod.Post, $"{path}/roles", body, cancellationToken);
            TenantRealm.EnsureSuccess(created, "create the operator role");
        }
    }

    /// <summary>
    /// Finds the account with this email or creates it with the required actions of the invitation, and gives it the role. A role that the
    /// account holds already stays as it is. <c>Created</c> is true when this call made the account.
    /// </summary>
    public async Task<(string UserId, bool Created)> EnsureOperatorAsync(string realm, string role, string email, CancellationToken cancellationToken)
    {
        var token = await _realm.AdminTokenAsync(cancellationToken);
        var path = RealmPath(realm);

        using var found = await _realm.SendAsync(
            token, HttpMethod.Get, $"{path}/users?email={Uri.EscapeDataString(email)}&exact=true&briefRepresentation=true", null, cancellationToken);
        TenantRealm.EnsureSuccess(found, "search the users by email");
        var matches = (await found.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();

        string userId;
        var created = matches.Count == 0;
        if (created)
        {
            var user = new { username = email, email, enabled = true, requiredActions = _invitationActions };
            using var response = await _realm.SendAsync(token, HttpMethod.Post, $"{path}/users", user, cancellationToken);
            TenantRealm.EnsureSuccess(response, "create the operator");
            var location = response.Headers.Location?.ToString()
                ?? throw new InvalidOperationException("The identity provider step 'create the operator' answered no location.");
            userId = location[(location.LastIndexOf('/') + 1)..];
        }
        else
        {
            userId = matches[0].GetProperty("id").GetString()!;
        }

        var representation = await ReadRoleAsync(token, path, role, cancellationToken)
            ?? throw new InvalidOperationException("The identity provider step 'read the operator role' found no role.");
        using var given = await _realm.SendAsync(
            token, HttpMethod.Post, $"{path}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm", new[] { representation }, cancellationToken);
        TenantRealm.EnsureSuccess(given, "give the operator role");
        return (userId, created);
    }

    /// <summary>Whether the account has set a password.</summary>
    public async Task<bool> HasPasswordAsync(string realm, string userId, CancellationToken cancellationToken)
    {
        var token = await _realm.AdminTokenAsync(cancellationToken);
        using var listed = await _realm.SendAsync(token, HttpMethod.Get, $"{UserPath(realm, userId)}/credentials", null, cancellationToken);
        TenantRealm.EnsureSuccess(listed, "list the credentials of the operator");
        var credentials = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray();
        return credentials.Any(credential => credential.TryGetProperty("type", out var type) && type.GetString() == "password");
    }

    /// <summary>
    /// Asks the realm to email the account a link that sets a password and adds an authenticator. The request names no client and no
    /// return address, so the realm's built-in <c>account</c> client handles the link.
    /// </summary>
    public async Task SendInvitationAsync(string realm, string userId, CancellationToken cancellationToken)
    {
        var token = await _realm.AdminTokenAsync(cancellationToken);
        using var response = await _realm.SendAsync(
            token,
            HttpMethod.Put,
            $"{UserPath(realm, userId)}/execute-actions-email?lifespan={RealmUsers.InvitationLifespanSeconds}",
            _invitationActions,
            cancellationToken);
        TenantRealm.EnsureSuccess(response, "send the invitation");
    }

    private async Task<JsonElement?> ReadRoleAsync(string token, string realmPath, string role, CancellationToken cancellationToken)
    {
        using var response = await _realm.SendAsync(token, HttpMethod.Get, $"{realmPath}/roles/{Uri.EscapeDataString(role)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        TenantRealm.EnsureSuccess(response, "read the operator role");
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static string RealmPath(string realm) => $"admin/realms/{Uri.EscapeDataString(realm)}";

    private static string UserPath(string realm, string userId) => $"{RealmPath(realm)}/users/{Uri.EscapeDataString(userId)}";
}
