using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Lists and changes the users of a tenant's realm. It signs in to the realm as <c>axis-users</c> with the client
/// credentials grant. That client's service account views, queries and manages users, and nothing else in the realm.
/// It reads one page of users with the number of all users that match the search, and for each user on the page the
/// alias of the first federated identity. It also finds one user, disables or enables a user, ends a user's realm
/// sessions and removes a user's authenticators. It also invites a person: it finds an account by email, creates an account with
/// the required actions of the invitation, asks the realm to email the link, reads what a resend needs and deletes a user. It
/// also normalises an email address. An error names the step and the status code, never a response body or a secret.
/// </summary>
public sealed class RealmUsers(HttpClient http, TenantRealmUsers access)
{
    private const string UsersClientId = "axis-users";

    /// <summary>The client whose redirect addresses the realm accepts as the place an invitation link leads to.</summary>
    public const string PlatformClientId = "axis";

    /// <summary>How long an invitation link works: 72 hours.</summary>
    public const int InvitationLifespanSeconds = 259200;

    private static readonly string[] _invitationActions = ["UPDATE_PASSWORD", "CONFIGURE_TOTP"];

    /// <summary>
    /// Reads the users that match <paramref name="search"/> in the name, username or email, or every user when it is
    /// empty, sorted by the identity provider. A service account is not a user of the list.
    /// </summary>
    /// <param name="first">How many matching users to skip.</param>
    /// <param name="max">The most users to read.</param>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task<RealmUserPage> ListAsync(string? search, int first, int max, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var usersPath = $"admin/realms/{Uri.EscapeDataString(access.Realm)}/users";

        // A search of * matches every user and leaves service accounts out, which an empty search does not.
        var query = $"search={Uri.EscapeDataString(string.IsNullOrWhiteSpace(search) ? "*" : search.Trim())}";
        using var listed = await GetAsync(token, $"{usersPath}?briefRepresentation=true&first={first}&max={max}&{query}", cancellationToken);
        EnsureSuccess(listed, "list the users");
        var users = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();

        using var counted = await GetAsync(token, $"{usersPath}/count?{query}", cancellationToken);
        EnsureSuccess(counted, "count the users");
        var total = await counted.Content.ReadFromJsonAsync<int>(cancellationToken);

        var aliases = await Task.WhenAll(users.Select(user => ProviderAliasAsync(token, usersPath, user.GetProperty("id").GetString()!, cancellationToken)));
        return new RealmUserPage(
            [.. users.Select((user, index) => new RealmUser(
                user.GetProperty("id").GetString()!,
                user.GetProperty("username").GetString()!,
                Text(user, "firstName"),
                Text(user, "lastName"),
                Text(user, "email"),
                user.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
                aliases[index]))],
            total);
    }

    /// <summary>
    /// Reads one user's state, or <c>null</c> when the realm has no user with this id or the user is a service account.
    /// </summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task<RealmUserState?> FindAsync(string userId, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var path = UserPath(userId);
        using var response = await GetAsync(token, path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, "read the user");
        var user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // A user's representation does not tell a service account. The search leaves service accounts out, and an exact search finds the user by name.
        var username = Text(user, "username") ?? "";
        using var found = await GetAsync(
            token, $"admin/realms/{Uri.EscapeDataString(access.Realm)}/users?briefRepresentation=true&max=100&search={Uri.EscapeDataString($"\"{username}\"")}", cancellationToken);
        EnsureSuccess(found, "search the user");
        var matches = await found.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!matches.EnumerateArray().Any(match => Text(match, "id") == userId))
        {
            return null;
        }

        return new RealmUserState(
            userId,
            user.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
            await ProviderAliasAsync(token, $"admin/realms/{Uri.EscapeDataString(access.Realm)}/users", userId, cancellationToken));
    }

    /// <summary>Disables or enables the user. It reads the full representation and writes it back, because a partial update can clear fields.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var path = UserPath(userId);
        using var read = await GetAsync(token, path, cancellationToken);
        EnsureSuccess(read, "read the user");
        var user = (await read.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!.AsObject();
        user["enabled"] = enabled;
        using var written = await SendAsync(token, HttpMethod.Put, path, user, cancellationToken);
        EnsureSuccess(written, enabled ? "enable the user" : "disable the user");
    }

    /// <summary>Ends every session of the user in the realm.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task LogOutAsync(string userId, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        using var response = await SendAsync(token, HttpMethod.Post, $"{UserPath(userId)}/logout", null, cancellationToken);
        EnsureSuccess(response, "end the realm sessions of the user");
    }

    /// <summary>Removes the user's authenticator app, so the next sign-in asks for a new one.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task RemoveAuthenticatorsAsync(string userId, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var path = UserPath(userId);
        using var listed = await GetAsync(token, $"{path}/credentials", cancellationToken);
        EnsureSuccess(listed, "list the credentials of the user");
        var credentials = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();
        foreach (var credential in credentials.Where(credential => Text(credential, "type") == "otp"))
        {
            using var removed = await SendAsync(
                token, HttpMethod.Delete, $"{path}/credentials/{Uri.EscapeDataString(credential.GetProperty("id").GetString()!)}", null, cancellationToken);
            EnsureSuccess(removed, "remove the authenticator of the user");
        }
    }

    /// <summary>
    /// Creates an account for the invitation, with its email as the username and the required actions of a password and an
    /// authenticator, and returns its id. The realm sends nothing yet. <paramref name="firstName"/> holds the whole display name.
    /// </summary>
    /// <exception cref="RealmUserConflictException">The realm already has a user with this username.</exception>
    /// <exception cref="InvalidOperationException">An identity provider step answered another error status.</exception>
    public async Task<string> CreateInvitedAsync(string email, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        using var response = await SendAsync(
            token,
            HttpMethod.Post,
            $"admin/realms/{Uri.EscapeDataString(access.Realm)}/users",
            new
            {
                username = email,
                email,
                firstName,
                lastName,
                enabled = true,
                requiredActions = _invitationActions,
            },
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new RealmUserConflictException();
        }

        EnsureSuccess(response, "create the invited user");
        var location = response.Headers.Location?.ToString() ?? throw new InvalidOperationException("The identity provider step 'create the invited user' answered no location.");
        return location[(location.LastIndexOf('/') + 1)..];
    }

    /// <summary>
    /// Trims and lower-cases an email address and returns it, or <c>null</c> when it is empty or not a plain address such as
    /// <c>name@example.test</c>. The realm stores the username and email in lower case.
    /// </summary>
    public static string? NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        return email is not null && MailAddress.TryCreate(email, out var address) && address.Address == email ? email : null;
    }

    /// <summary>Whether an account, a user with no federated identity, uses this email. A user of a brokered provider does not count.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task<bool> AccountUsesEmailAsync(string email, CancellationToken cancellationToken) =>
        await FindAccountIdByEmailAsync(email, cancellationToken) is not null;

    /// <summary>The id of the first account, a user with no federated identity, whose email is exactly this one, or <c>null</c> when there is none.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task<string?> FindAccountIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var usersPath = $"admin/realms/{Uri.EscapeDataString(access.Realm)}/users";
        using var response = await GetAsync(
            token, $"{usersPath}?email={Uri.EscapeDataString(email)}&exact=true&briefRepresentation=true", cancellationToken);
        EnsureSuccess(response, "search the users by email");
        var matches = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray().ToList();
        foreach (var match in matches)
        {
            var id = match.GetProperty("id").GetString()!;
            if (await ProviderAliasAsync(token, usersPath, id, cancellationToken) is null)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Asks the realm to email the user a link that sets a password and adds an authenticator, and that leads to
    /// <paramref name="redirectUri"/>. A <c>null</c> address names none, for a caller that has no address the realm accepts.
    /// </summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task SendInvitationAsync(string userId, Uri? redirectUri, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var query = $"lifespan={InvitationLifespanSeconds}&client_id={PlatformClientId}";
        if (redirectUri is not null)
        {
            query += $"&redirect_uri={Uri.EscapeDataString(redirectUri.AbsoluteUri)}";
        }

        using var response = await SendAsync(token, HttpMethod.Put, $"{UserPath(userId)}/execute-actions-email?{query}", _invitationActions, cancellationToken);
        EnsureSuccess(response, "send the invitation");
    }

    /// <summary>Reads the fields of a user that a resend carries over and whether the user has a password.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task<RealmInvitee> ReadInviteeAsync(string userId, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var path = UserPath(userId);
        using var read = await GetAsync(token, path, cancellationToken);
        EnsureSuccess(read, "read the user");
        var user = await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var listed = await GetAsync(token, $"{path}/credentials", cancellationToken);
        EnsureSuccess(listed, "list the credentials of the user");
        var credentials = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray();
        return new RealmInvitee(
            Text(user, "email"), Text(user, "firstName"), Text(user, "lastName"), credentials.Any(credential => Text(credential, "type") == "password"));
    }

    /// <summary>Deletes the user from the realm, which ends every link and session of the user.</summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status.</exception>
    public async Task DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        using var response = await SendAsync(token, HttpMethod.Delete, UserPath(userId), null, cancellationToken);
        EnsureSuccess(response, "delete the user");
    }

    private string UserPath(string userId) =>
        $"admin/realms/{Uri.EscapeDataString(access.Realm)}/users/{Uri.EscapeDataString(userId)}";

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = UsersClientId,
            ["client_secret"] = access.ClientSecret.Reveal(),
        });
        using var response = await http.PostAsync(
            new Uri(access.BaseAddress, $"realms/{Uri.EscapeDataString(access.Realm)}/protocol/openid-connect/token"), content, cancellationToken);
        EnsureSuccess(response, "get the users token");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<string?> ProviderAliasAsync(string token, string usersPath, string userId, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(token, $"{usersPath}/{Uri.EscapeDataString(userId)}/federated-identity", cancellationToken);
        EnsureSuccess(response, "read the identities of a user");
        var identities = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return identities.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first
            ? Text(first, "identityProvider")
            : null;
    }

    private Task<HttpResponseMessage> GetAsync(string token, string path, CancellationToken cancellationToken) =>
        SendAsync(token, HttpMethod.Get, path, null, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(access.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string step) => RealmSync.EnsureSuccess(response, step);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}

/// <summary>One page of a tenant's realm users and the number of all users that match the search.</summary>
public sealed record RealmUserPage(IReadOnlyList<RealmUser> Users, int TotalCount);

/// <summary>A user of a tenant's realm. <c>ProviderAlias</c> is the alias of the first federated identity, or <c>null</c> for an account.</summary>
public sealed record RealmUser(string Id, string Username, string? FirstName, string? LastName, string? Email, bool Enabled, string? ProviderAlias);

/// <summary>The state of one realm user that a command needs. <c>ProviderAlias</c> is the alias of the first federated identity, or <c>null</c> for an account.</summary>
public sealed record RealmUserState(string Id, bool Enabled, string? ProviderAlias);

/// <summary>The fields of an invited user that a resend carries over, and whether the user has set a password.</summary>
public sealed record RealmInvitee(string? Email, string? FirstName, string? LastName, bool HasPassword);

/// <summary>The realm already has a user with the username an invitation asked for.</summary>
public sealed class RealmUserConflictException() : InvalidOperationException("The identity provider step 'create the invited user' failed with status 409.");
