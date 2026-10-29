using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Lists the users of a tenant's realm. It signs in to the realm as <c>axis-users</c> with the client credentials grant.
/// That client's service account can only view and query users. It reads one page of users with the number of all
/// users that match the search, and for each user on the page the alias of the first federated identity. An error
/// names the step and the status code, never a response body or a secret.
/// </summary>
public sealed class RealmUsers(HttpClient http, TenantRealmUsers access)
{
    private const string UsersClientId = "axis-users";

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

    private async Task<HttpResponseMessage> GetAsync(string token, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(access.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
