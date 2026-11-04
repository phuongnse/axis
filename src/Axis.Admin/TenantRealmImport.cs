using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Admin;

/// <summary>
/// Brings <c>realm.json</c> of a backup into the new realm of a restored tenant with the identity provider's partial import, skipping what
/// exists: first the roles and the groups, then the users in batches. A user keeps the id, the password hash and the authenticators that the
/// document holds. The document does not say which role is the default one: the old default role is named <c>default-roles-</c> and the old realm
/// name in lower case, so a user or group that held it holds the new realm's default role instead. A role of a client that the new realm lacks is
/// dropped from the roles, the composites and the mappings, because the partial import fails on it. It calls the admin API as the master admin
/// client, which has the manage-realm and manage-users rights that the import of users needs. An error names the step and the status, never a
/// response body, and the document is never logged. <see cref="Read"/> checks the document, before any tenant work.
/// </summary>
internal sealed class TenantRealmImport(TenantRealm realm)
{
    public const long MaxBytes = 64L * 1024 * 1024;

    public const string TooLarge = "the archive's realm.json is larger than 64 MiB";

    public const string Invalid = "the archive's realm.json is not valid";

    private const int UserBatchSize = 100;

    private const int ClientPageSize = 1000;

    private const string DefaultRolePrefix = "default-roles-";

    /// <summary>
    /// Returns the document, or null and one of the two messages. A length above <see cref="MaxBytes"/> is refused before anything is read.
    /// The document is valid when it has the shape that <see cref="TenantRealmExport"/> writes, as far as the import reads it.
    /// </summary>
    public static (JsonObject? Document, string? Problem) Read(Stream data, long length)
    {
        if (length > MaxBytes)
        {
            return (null, TooLarge);
        }

        try
        {
            return JsonNode.Parse(data) is JsonObject document && IsValid(document) ? (document, null) : (null, Invalid);
        }
        catch (JsonException)
        {
            return (null, Invalid);
        }
    }

    /// <summary>
    /// Imports <paramref name="document"/> into the realm <paramref name="target"/>. With <paramref name="newIds"/>, the users and their credentials
    /// lose their ids and the identity provider makes new ones, because those ids are global and a second realm cannot import the ids of a live one.
    /// </summary>
    public async Task ImportAsync(string target, JsonObject document, bool newIds, CancellationToken cancellationToken)
    {
        var token = await realm.AdminTokenAsync(cancellationToken);
        var realmPath = $"admin/realms/{Uri.EscapeDataString(target)}";

        using var read = await realm.SendAsync(token, HttpMethod.Get, realmPath, null, cancellationToken);
        TenantRealm.EnsureSuccess(read, "read the realm");
        var current = await read.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var newDefault = current?["defaultRole"]?["name"]?.GetValue<string>()
            ?? throw new InvalidOperationException("The identity provider step 'read the default role' found no role.");
        var clients = await ClientIdsAsync(token, realmPath, cancellationToken);

        var prepared = (JsonObject)document.DeepClone();
        Prepare(prepared, DefaultRolePrefix + ((string)document["realm"]!).ToLowerInvariant(), newDefault, clients, newIds);

        using var structure = await realm.SendAsync(
            token,
            HttpMethod.Post,
            $"{realmPath}/partialImport",
            new JsonObject { ["ifResourceExists"] = "SKIP", ["roles"] = prepared["roles"]?.DeepClone(), ["groups"] = prepared["groups"]?.DeepClone() },
            cancellationToken);
        TenantRealm.EnsureSuccess(structure, "import the roles and groups");

        var users = prepared["users"]!.AsArray();
        for (var start = 0; start < users.Count; start += UserBatchSize)
        {
            var batch = new JsonArray([.. users.Skip(start).Take(UserBatchSize).Select(user => user!.DeepClone())]);
            using var imported = await realm.SendAsync(
                token,
                HttpMethod.Post,
                $"{realmPath}/partialImport",
                new JsonObject { ["ifResourceExists"] = "SKIP", ["users"] = batch },
                cancellationToken);
            TenantRealm.EnsureSuccess(imported, "import the users");
        }
    }

    private static bool IsValid(JsonObject root) =>
        IsText(root["realm"])
        && root["roles"] is JsonObject roles
        && IsRoleList(roles["realm"])
        && roles["client"] is JsonObject clientRoles
        && clientRoles.All(client => IsRoleList(client.Value))
        && root["groups"] is JsonArray groups
        && groups.All(group => group is JsonObject)
        && root["users"] is JsonArray users
        && users.All(user => user is JsonObject member && IsText(member["id"]) && IsText(member["username"]));

    private static bool IsRoleList(JsonNode? node) =>
        node is JsonArray roles && roles.All(role => role is JsonObject item && IsText(item["name"]));

    private static bool IsText(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0;

    private async Task<HashSet<string>> ClientIdsAsync(string token, string realmPath, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var first = 0; ; first += ClientPageSize)
        {
            using var response = await realm.SendAsync(token, HttpMethod.Get, $"{realmPath}/clients?first={first}&max={ClientPageSize}", null, cancellationToken);
            TenantRealm.EnsureSuccess(response, "list the clients");
            var page = (await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken))!;
            foreach (var client in page)
            {
                ids.Add(client!["clientId"]!.GetValue<string>());
            }

            if (page.Count < ClientPageSize)
            {
                return ids;
            }
        }
    }

    // Makes the document fit the new realm, in place: the old default role goes, and so do the roles of clients that the realm lacks.
    private static void Prepare(JsonObject document, string oldDefault, string newDefault, HashSet<string> clients, bool newIds)
    {
        var roles = document["roles"]!.AsObject();
        var realmRoles = roles["realm"]!.AsArray();
        foreach (var role in realmRoles.Where(role => (string?)role!["name"] == oldDefault).ToList())
        {
            realmRoles.Remove(role);
        }

        var clientRoles = roles["client"]!.AsObject();
        foreach (var clientId in clientRoles.Select(client => client.Key).Where(clientId => !clients.Contains(clientId)).ToList())
        {
            clientRoles.Remove(clientId);
        }

        foreach (var role in realmRoles.Concat(clientRoles.SelectMany(client => client.Value!.AsArray())))
        {
            if (role!["composites"] is JsonObject composites)
            {
                Remap(composites, oldDefault, newDefault, clients);
            }
        }

        foreach (var group in document["groups"]!.AsArray())
        {
            PrepareGroup(group!.AsObject(), oldDefault, newDefault, clients);
        }

        foreach (var user in document["users"]!.AsArray())
        {
            var member = user!.AsObject();
            Remap(member, oldDefault, newDefault, clients, "realmRoles", "clientRoles");
            if (newIds)
            {
                member.Remove("id");
                if (member["credentials"] is JsonArray credentials)
                {
                    foreach (var credential in credentials.OfType<JsonObject>())
                    {
                        credential.Remove("id");
                    }
                }
            }
        }
    }

    private static void PrepareGroup(JsonObject group, string oldDefault, string newDefault, HashSet<string> clients)
    {
        Remap(group, oldDefault, newDefault, clients, "realmRoles", "clientRoles");
        if (group["subGroups"] is JsonArray children)
        {
            foreach (var child in children)
            {
                PrepareGroup(child!.AsObject(), oldDefault, newDefault, clients);
            }
        }
    }

    // A composite names its roles as realm and client, a user or a group as realmRoles and clientRoles.
    private static void Remap(JsonObject holder, string oldDefault, string newDefault, HashSet<string> clients, string realmField = "realm", string clientField = "client")
    {
        if (holder[realmField] is JsonArray names && names.Any(name => (string?)name == oldDefault))
        {
            var kept = names.Where(name => (string?)name != oldDefault).Select(name => name!.DeepClone()).ToList();
            if (!kept.Any(name => (string?)name == newDefault))
            {
                kept.Add(JsonValue.Create(newDefault));
            }

            holder[realmField] = new JsonArray([.. kept]);
        }

        if (holder[clientField] is JsonObject held)
        {
            foreach (var clientId in held.Select(client => client.Key).Where(clientId => !clients.Contains(clientId)).ToList())
            {
                held.Remove(clientId);
            }
        }
    }
}
