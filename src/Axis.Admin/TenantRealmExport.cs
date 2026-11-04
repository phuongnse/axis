using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Admin;

/// <summary>
/// Reads what a restore needs to bring a tenant's realm back and builds <c>realm.json</c> in memory: the realm roles and the roles of the
/// <c>axis</c> and <c>site-</c> clients, the groups, and the users with their role mappings, groups, required actions and credentials. It has no
/// clients, because activation makes them again, and no service-account user. Roles come from the admin API as <c>axis-activation</c>, and
/// groups and users as <c>axis-users</c>, both with the client credentials grant. Credentials, the password hashes and authenticator secrets, which the
/// admin API never returns, come from Keycloak's <c>credential</c> table through a read-only connection, for the users that the admin API returned. Its field
/// names are those of the identity provider's realm export. Each part is named here, so a new field of the identity provider never reaches the archive by
/// accident. An error names the step and the status, never a response body, a host or a secret, and the document is returned to the caller and never written or logged here.
/// </summary>
internal sealed class TenantRealmExport(HttpClient http, TenantRealmAccess activation, TenantRealmUsers users, string credentialsConnectionString)
{
    private const int UserPageSize = 100;

    private const int ListLimit = 1000;

    private static readonly string[] _roleFields = ["name", "description", "composite", "attributes"];

    private static readonly string[] _groupFields = ["name", "path", "attributes"];

    private static readonly string[] _userFields =
    [
        "id", "username", "email", "emailVerified", "firstName", "lastName", "enabled", "createdTimestamp", "attributes", "requiredActions",
    ];

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var activationToken = await TokenAsync(activation.BaseAddress, activation.Name, TenantRealm.ActivationClientId, activation.ActivationClientSecret.Reveal(), cancellationToken);
        var usersToken = await TokenAsync(users.BaseAddress, users.Realm, TenantRealm.UsersClientId, users.ClientSecret.Reveal(), cancellationToken);

        var roles = await ReadRolesAsync(activationToken, cancellationToken);
        var groups = await ReadGroupsAsync(usersToken, cancellationToken);
        var members = await ReadUsersAsync(usersToken, cancellationToken);
        var credentials = await ReadCredentialsAsync([.. members.Select(user => user["id"]!.GetValue<string>())], cancellationToken);
        foreach (var user in members)
        {
            user["credentials"] = credentials.TryGetValue(user["id"]!.GetValue<string>(), out var held) ? held : new JsonArray();
        }

        var document = new JsonObject
        {
            ["realm"] = users.Realm,
            ["roles"] = roles,
            ["groups"] = groups,
            ["users"] = new JsonArray([.. members]),
        };
        return JsonSerializer.SerializeToUtf8Bytes(document);
    }

    private static bool Kept(string clientId) =>
        clientId == TenantRealm.PlatformClientId || clientId.StartsWith("site-", StringComparison.Ordinal);

    private async Task<JsonObject> ReadRolesAsync(string activationToken, CancellationToken cancellationToken)
    {
        var realmPath = RealmPath(activation.BaseAddress, activation.Name);
        var clients = await ListAsync(activationToken, $"{realmPath}/clients", "briefRepresentation=false", "list the clients", cancellationToken);
        var clientIds = clients.ToDictionary(client => client["id"]!.GetValue<string>(), client => client["clientId"]!.GetValue<string>(), StringComparer.Ordinal);

        var realmRoles = new JsonArray();
        foreach (var role in await ListAsync(activationToken, $"{realmPath}/roles", "briefRepresentation=false", "list the realm roles", cancellationToken))
        {
            realmRoles.Add(await RoleAsync(activationToken, role, clientIds, cancellationToken));
        }

        var clientRoles = new JsonObject();
        foreach (var client in clients.Where(client => Kept(client["clientId"]!.GetValue<string>())).OrderBy(client => client["clientId"]!.GetValue<string>(), StringComparer.Ordinal))
        {
            var held = new JsonArray();
            var roles = await ListAsync(
                activationToken, $"{realmPath}/clients/{Uri.EscapeDataString(client["id"]!.GetValue<string>())}/roles", "briefRepresentation=false", "list the client roles", cancellationToken);
            foreach (var role in roles)
            {
                held.Add(await RoleAsync(activationToken, role, clientIds, cancellationToken));
            }

            clientRoles[client["clientId"]!.GetValue<string>()] = held;
        }

        return new JsonObject { ["realm"] = realmRoles, ["client"] = clientRoles };
    }

    private async Task<JsonObject> RoleAsync(string token, JsonNode source, Dictionary<string, string> clientIds, CancellationToken cancellationToken)
    {
        var role = Pick(source, _roleFields);
        if (source["composite"]?.GetValue<bool>() == true)
        {
            var realm = new JsonArray();
            var client = new JsonObject();
            var path = $"{RealmPath(activation.BaseAddress, activation.Name)}/roles-by-id/{Uri.EscapeDataString(source["id"]!.GetValue<string>())}/composites";
            // The composites are not paged by the identity provider.
            foreach (var child in (await GetAsync(token, path, "list the composite roles", cancellationToken)).AsArray())
            {
                var name = child!["name"]!.GetValue<string>();
                if (child["clientRole"]?.GetValue<bool>() != true)
                {
                    realm.Add(name);
                    continue;
                }

                var container = child["containerId"]!.GetValue<string>();
                var clientId = clientIds.TryGetValue(container, out var found) ? found : container;
                if (client[clientId] is not JsonArray names)
                {
                    client[clientId] = names = [];
                }

                names.Add(name);
            }

            role["composites"] = new JsonObject { ["realm"] = realm, ["client"] = client };
        }

        return role;
    }

    private async Task<JsonArray> ReadGroupsAsync(string token, CancellationToken cancellationToken)
    {
        var realmPath = RealmPath(users.BaseAddress, users.Realm);
        var groups = new JsonArray();
        foreach (var group in await ListAsync(token, $"{realmPath}/groups", "briefRepresentation=false", "list the groups", cancellationToken))
        {
            groups.Add(await GroupAsync(token, realmPath, group, cancellationToken));
        }

        return groups;
    }

    private async Task<JsonObject> GroupAsync(string token, string realmPath, JsonNode source, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(source["id"]!.GetValue<string>());
        var group = Pick(source, _groupFields);
        var (realmRoles, clientRoles) = await MappingsAsync(token, $"{realmPath}/groups/{id}/role-mappings", "read the group role mappings", cancellationToken);
        group["realmRoles"] = realmRoles;
        group["clientRoles"] = clientRoles;

        var subGroups = new JsonArray();
        foreach (var child in await ListAsync(token, $"{realmPath}/groups/{id}/children", "briefRepresentation=false", "list the subgroups", cancellationToken))
        {
            subGroups.Add(await GroupAsync(token, realmPath, child, cancellationToken));
        }

        group["subGroups"] = subGroups;
        return group;
    }

    // A search of * matches every user and leaves service accounts out, and a user that has a service account client is skipped as well.
    private async Task<List<JsonObject>> ReadUsersAsync(string token, CancellationToken cancellationToken)
    {
        var realmPath = RealmPath(users.BaseAddress, users.Realm);
        var result = new List<JsonObject>();
        foreach (var source in await ListAsync(token, $"{realmPath}/users", "search=*&briefRepresentation=false", "list the users", cancellationToken, UserPageSize))
        {
            if (source["serviceAccountClientId"] is not null)
            {
                continue;
            }

            var id = Uri.EscapeDataString(source["id"]!.GetValue<string>());
            var user = Pick(source, _userFields);
            var (realmRoles, clientRoles) = await MappingsAsync(token, $"{realmPath}/users/{id}/role-mappings", "read the user role mappings", cancellationToken);
            user["realmRoles"] = realmRoles;
            user["clientRoles"] = clientRoles;
            user["groups"] = new JsonArray([.. (await GetAsync(token, $"{realmPath}/users/{id}/groups", "read the user groups", cancellationToken))
                .AsArray().Select(group => JsonValue.Create(group!["path"]!.GetValue<string>()))]);
            result.Add(user);
        }

        return result;
    }

    // The realm roles and the roles of the kept clients that a user or a group holds, as the realm export names them.
    private async Task<(JsonArray Realm, JsonObject Client)> MappingsAsync(string token, string path, string step, CancellationToken cancellationToken)
    {
        var mappings = await GetAsync(token, path, step, cancellationToken);
        var realm = new JsonArray([.. (mappings["realmMappings"]?.AsArray() ?? new JsonArray()).Select(role => JsonValue.Create(role!["name"]!.GetValue<string>()))]);
        var client = new JsonObject();
        foreach (var (clientId, held) in mappings["clientMappings"]?.AsObject() ?? new JsonObject())
        {
            if (Kept(clientId))
            {
                client[clientId] = new JsonArray([.. held!["mappings"]!.AsArray().Select(role => JsonValue.Create(role!["name"]!.GetValue<string>()))]);
            }
        }

        return (realm, client);
    }

    private async Task<Dictionary<string, JsonArray>> ReadCredentialsAsync(string[] userIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        if (userIds.Length == 0)
        {
            return result;
        }

        var builder = new NpgsqlConnectionStringBuilder(credentialsConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT user_id, id, type, user_label, created_date, secret_data, credential_data, priority FROM credential WHERE user_id = ANY(@ids) ORDER BY user_id, priority",
            connection);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = userIds });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userId = reader.GetString(0);
            if (!result.TryGetValue(userId, out var held))
            {
                result[userId] = held = [];
            }

            held.Add(new JsonObject
            {
                ["id"] = reader.GetString(1),
                ["type"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                ["userLabel"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                ["createdDate"] = reader.IsDBNull(4) ? null : (long?)reader.GetInt64(4),
                ["secretData"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                ["credentialData"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                ["priority"] = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7),
            });
        }

        return result;
    }

    private static string RealmPath(Uri baseAddress, string realm) =>
        new Uri(baseAddress, $"admin/realms/{Uri.EscapeDataString(realm)}").AbsoluteUri;

    // The named fields of the source that it has and that are not null, copied so that the result holds no node of the response.
    private static JsonObject Pick(JsonNode source, string[] fields)
    {
        var picked = new JsonObject();
        foreach (var field in fields)
        {
            if (source[field] is { } value)
            {
                picked[field] = value.DeepClone();
            }
        }

        return picked;
    }

    private async Task<string> TokenAsync(Uri baseAddress, string realm, string clientId, string secret, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await http.PostAsync(
            new Uri(baseAddress, $"realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token"), content, cancellationToken);
        TenantRealm.EnsureSuccess(response, $"get the {clientId} token");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    // Reads every page of a list, so a realm with more than a page of groups or users loses none.
    private async Task<List<JsonNode>> ListAsync(string token, string path, string query, string step, CancellationToken cancellationToken, int size = ListLimit)
    {
        var all = new List<JsonNode>();
        for (var first = 0; ; first += size)
        {
            var page = (await GetAsync(token, path, step, cancellationToken, $"{query}&first={first}&max={size}")).AsArray();
            all.AddRange(page.Select(item => item!));
            if (page.Count < size)
            {
                return all;
            }
        }
    }

    private async Task<JsonNode> GetAsync(string token, string path, string step, CancellationToken cancellationToken, string? query = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(query is null ? path : $"{path}?{query}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        TenantRealm.EnsureSuccess(response, step);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }
}
