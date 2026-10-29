using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Makes a tenant's realm hold the site clients and brokered <c>oidc</c> providers of a <see cref="RealmState"/>. It
/// signs in to the realm as <c>axis-activation</c> with the client credentials grant, so it needs no rights in the
/// master realm. Each site gets a confidential client <c>site-</c> and its path, whose ID token carries the alias of the
/// provider that signed the person in as the <c>identity_provider</c> claim. Each provider gets claim mappers for the
/// display name, the email, the locale and each roles claim, and a <c>username</c> mapper that names a new user after the
/// provider's alias and the person's subject. Each provider also names the realm's own first-login flow
/// (<see cref="FirstLoginFlowAlias"/>), which always creates a new user and never offers to link one. The sync adds, changes and removes only clients whose id
/// starts with <see cref="SiteClientPrefix"/> and providers whose alias starts with <see cref="ProviderAliasPrefix"/>,
/// so <c>axis</c>, <c>axis-activation</c>, <c>axis-users</c> and anything an operator added stay. A removed provider
/// keeps its users in the realm. An error names the step and the status code, never a response body or a secret.
/// </summary>
public sealed class RealmSync(HttpClient http, Uri baseAddress)
{
    public const string SiteClientPrefix = "site-";
    public const string ProviderAliasPrefix = "oidc-";
    public const string IdentityProviderClaim = "identity_provider";

    /// <summary>The alias of the first-login flow that <c>TenantRealm</c> makes and every provider names.</summary>
    public const string FirstLoginFlowAlias = "axis first login";

    private const string ActivationClientId = "axis-activation";
    private const string UserAttributeMapper = "oidc-user-attribute-idp-mapper";
    private const string UsernameMapper = "oidc-username-idp-mapper";
    private const string UsernameMapperName = "username";
    private const string SessionNoteMapper = "oidc-usersessionmodel-note-mapper";
    private const string SessionNoteMapperName = "identity provider";

    public static string SiteClientId(string path) => SiteClientPrefix + path;

    public async Task SyncAsync(string realm, string activationSecret, RealmState state, CancellationToken cancellationToken)
    {
        Validate(state);

        var token = await ActivationTokenAsync(realm, activationSecret, cancellationToken);
        var realmPath = $"admin/realms/{Uri.EscapeDataString(realm)}";

        // Every discovery document is read before the realm changes, so an unreachable issuer changes nothing.
        var providers = new List<(RealmBrokeredProvider Provider, Dictionary<string, string> Endpoints)>();
        foreach (var provider in state.Providers)
        {
            providers.Add((provider, await ReadDiscoveryAsync(provider, cancellationToken)));
        }

        await SyncSiteClientsAsync(token, realmPath, state.SitePaths, cancellationToken);
        await SyncProvidersAsync(token, realmPath, providers, cancellationToken);
    }

    private static void Validate(RealmState state)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in state.SitePaths)
        {
            if (!paths.Add(path))
            {
                throw new ArgumentException($"The site path '{path}' appears twice.", nameof(state));
            }
        }

        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in state.Providers)
        {
            if (!provider.Alias.StartsWith(ProviderAliasPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException($"The provider alias '{provider.Alias}' does not start with '{ProviderAliasPrefix}'.", nameof(state));
            }

            if (!aliases.Add(provider.Alias))
            {
                throw new ArgumentException($"The provider alias '{provider.Alias}' appears twice.", nameof(state));
            }
        }
    }

    internal async Task<string> ActivationTokenAsync(string realm, string activationSecret, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ActivationClientId,
            ["client_secret"] = activationSecret,
        });
        using var response = await http.PostAsync(
            new Uri(baseAddress, $"realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token"), content, cancellationToken);
        EnsureSuccess(response, "get the activation token");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task SyncSiteClientsAsync(string token, string realmPath, IReadOnlyList<string> sitePaths, CancellationToken cancellationToken)
    {
        using var listed = await SendAsync(token, HttpMethod.Get, $"{realmPath}/clients?max=1000", null, cancellationToken);
        EnsureSuccess(listed, "list the clients");
        var existing = new Dictionary<string, (string Uuid, JsonElement Representation)>(StringComparer.Ordinal);
        JsonElement? platformClient = null;
        foreach (var client in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            var clientId = client.GetProperty("clientId").GetString()!;
            if (clientId.StartsWith(SiteClientPrefix, StringComparison.Ordinal))
            {
                existing[clientId] = (client.GetProperty("id").GetString()!, client);
            }
            else if (clientId == RealmClientProfile.PlatformClientId)
            {
                platformClient = client;
            }
        }

        var desired = new HashSet<string>(sitePaths.Select(SiteClientId), StringComparer.Ordinal);
        foreach (var clientId in desired)
        {
            string uuid;
            if (!existing.TryGetValue(clientId, out var current))
            {
                using var created = await SendAsync(token, HttpMethod.Post, $"{realmPath}/clients", SiteClient(clientId, platformClient, null), cancellationToken);
                EnsureSuccess(created, $"create the client '{clientId}'");
                uuid = created.Headers.Location?.Segments[^1].TrimEnd('/')
                    ?? throw new InvalidOperationException($"The identity provider step 'find the created client '{clientId}'' found no client.");
            }
            else
            {
                uuid = current.Uuid;
                using var updated = await SendAsync(token, HttpMethod.Put, $"{realmPath}/clients/{uuid}", SiteClient(clientId, platformClient, current.Representation), cancellationToken);
                EnsureSuccess(updated, $"update the client '{clientId}'");
            }

            await EnsureSessionNoteMapperAsync(token, realmPath, uuid, clientId, cancellationToken);
        }

        foreach (var (clientId, (uuid, _)) in existing.Where(pair => !desired.Contains(pair.Key)))
        {
            using var deleted = await SendAsync(token, HttpMethod.Delete, $"{realmPath}/clients/{uuid}", null, cancellationToken);
            EnsureSuccess(deleted, $"delete the client '{clientId}'");
        }
    }

    /// <summary>
    /// The secret is left out, so Keycloak makes one when it creates the client and keeps it on an update. When the realm has
    /// the platform client <c>axis</c>, the site client takes its client authentication and the settings of its sign-in profile
    /// and keys (<see cref="RealmClientProfile"/>), so activation never undoes what <c>tenant oidc set</c> wrote. An update
    /// keeps the other attributes of the existing client, because Keycloak removes the attributes that a client update leaves out.
    /// </summary>
    private static Dictionary<string, object?> SiteClient(string clientId, JsonElement? platformClient, JsonElement? existing)
    {
        var client = new Dictionary<string, object?>
        {
            ["clientId"] = clientId,
            ["enabled"] = true,
            ["protocol"] = "openid-connect",
            ["publicClient"] = false,
            ["clientAuthenticatorType"] = "client-secret",
            ["standardFlowEnabled"] = true,
            ["directAccessGrantsEnabled"] = false,
            ["implicitFlowEnabled"] = false,
            ["serviceAccountsEnabled"] = false,
        };
        if (platformClient is { } platform)
        {
            client["clientAuthenticatorType"] = RealmClientProfile.AuthenticatorTypeOf(platform);
            client["attributes"] = RealmClientProfile.AttributesFor(platform, existing);
        }

        return client;
    }

    private static Dictionary<string, string> SessionNoteConfig() => new()
    {
        ["user.session.note"] = IdentityProviderClaim,
        ["claim.name"] = IdentityProviderClaim,
        ["id.token.claim"] = "true",
        ["access.token.claim"] = "false",
        ["userinfo.token.claim"] = "false",
        ["jsonType.label"] = "String",
    };

    /// <summary>A client update does not replace protocol mappers, so this adds the mapper or corrects its config.</summary>
    private async Task EnsureSessionNoteMapperAsync(string token, string realmPath, string uuid, string clientId, CancellationToken cancellationToken)
    {
        var mappersPath = $"{realmPath}/clients/{uuid}/protocol-mappers/models";
        using var listed = await SendAsync(token, HttpMethod.Get, mappersPath, null, cancellationToken);
        EnsureSuccess(listed, $"list the mappers of the client '{clientId}'");
        var desired = SessionNoteConfig();
        foreach (var mapper in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            if (mapper.GetProperty("name").GetString() != SessionNoteMapperName)
            {
                continue;
            }

            if (mapper.GetProperty("protocolMapper").GetString() == SessionNoteMapper && ConfigMatches(mapper, desired))
            {
                return;
            }

            var representation = SessionNoteMapperRepresentation(desired);
            representation["id"] = mapper.GetProperty("id").GetString();
            using var updated = await SendAsync(
                token, HttpMethod.Put, $"{mappersPath}/{representation["id"]}", representation, cancellationToken);
            EnsureSuccess(updated, $"update the mapper of the client '{clientId}'");
            return;
        }

        using var created = await SendAsync(token, HttpMethod.Post, mappersPath, SessionNoteMapperRepresentation(desired), cancellationToken);
        EnsureSuccess(created, $"create the mapper of the client '{clientId}'");
    }

    private static Dictionary<string, object?> SessionNoteMapperRepresentation(Dictionary<string, string> config) => new()
    {
        ["name"] = SessionNoteMapperName,
        ["protocol"] = "openid-connect",
        ["protocolMapper"] = SessionNoteMapper,
        ["config"] = config,
    };

    private static bool ConfigMatches(JsonElement mapper, Dictionary<string, string> desired)
    {
        if (!mapper.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return desired.All(pair => config.TryGetProperty(pair.Key, out var value) && value.GetString() == pair.Value);
    }

    private async Task<Dictionary<string, string>> ReadDiscoveryAsync(RealmBrokeredProvider provider, CancellationToken cancellationToken)
    {
        var step = $"read the discovery document of '{provider.Alias}'";
        var address = new Uri(provider.Issuer.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        using var response = await http.GetAsync(address, cancellationToken);
        EnsureSuccess(response, step);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var endpoints = new Dictionary<string, string>();
        foreach (var name in new[] { "authorization_endpoint", "token_endpoint", "jwks_uri" })
        {
            endpoints[name] = RequiredString(document, name, step);
        }

        foreach (var name in new[] { "userinfo_endpoint", "end_session_endpoint" })
        {
            if (document.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                endpoints[name] = value.GetString()!;
            }
        }

        return endpoints;
    }

    private static string RequiredString(JsonElement document, string name, string step) =>
        document.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"The identity provider step '{step}' found no '{name}'.");

    private async Task SyncProvidersAsync(
        string token,
        string realmPath,
        List<(RealmBrokeredProvider Provider, Dictionary<string, string> Endpoints)> providers,
        CancellationToken cancellationToken)
    {
        using var listed = await SendAsync(token, HttpMethod.Get, $"{realmPath}/identity-provider/instances", null, cancellationToken);
        EnsureSuccess(listed, "list the identity providers");
        var existing = new List<string>();
        foreach (var instance in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            var alias = instance.GetProperty("alias").GetString()!;
            if (alias.StartsWith(ProviderAliasPrefix, StringComparison.Ordinal))
            {
                existing.Add(alias);
            }
        }

        foreach (var (provider, endpoints) in providers)
        {
            var instancePath = $"{realmPath}/identity-provider/instances/{Uri.EscapeDataString(provider.Alias)}";
            var representation = ProviderRepresentation(provider, endpoints);
            if (existing.Contains(provider.Alias))
            {
                using var updated = await SendAsync(token, HttpMethod.Put, instancePath, representation, cancellationToken);
                EnsureSuccess(updated, $"update the identity provider '{provider.Alias}'");
            }
            else
            {
                using var created = await SendAsync(
                    token, HttpMethod.Post, $"{realmPath}/identity-provider/instances", representation, cancellationToken);
                EnsureSuccess(created, $"create the identity provider '{provider.Alias}'");
            }

            await SyncMappersAsync(token, instancePath, provider, cancellationToken);
        }

        var desired = providers.Select(entry => entry.Provider.Alias).ToHashSet(StringComparer.Ordinal);
        foreach (var alias in existing.Where(alias => !desired.Contains(alias)))
        {
            // Keycloak removes only the users' links to the provider, so the users stay.
            using var deleted = await SendAsync(
                token, HttpMethod.Delete, $"{realmPath}/identity-provider/instances/{Uri.EscapeDataString(alias)}", null, cancellationToken);
            EnsureSuccess(deleted, $"delete the identity provider '{alias}'");
        }
    }

    private static Dictionary<string, object?> ProviderRepresentation(RealmBrokeredProvider provider, Dictionary<string, string> endpoints)
    {
        var config = new Dictionary<string, string>
        {
            ["issuer"] = provider.Issuer.AbsoluteUri,
            ["authorizationUrl"] = endpoints["authorization_endpoint"],
            ["tokenUrl"] = endpoints["token_endpoint"],
            ["jwksUrl"] = endpoints["jwks_uri"],
            ["useJwksUrl"] = "true",
            ["validateSignature"] = "true",
            ["clientId"] = provider.ClientId,
            ["clientSecret"] = provider.ClientSecret,
            ["clientAuthMethod"] = "client_secret_post",
            ["defaultScope"] = "openid profile email",
            ["syncMode"] = "FORCE",
            ["pkceEnabled"] = "true",
            ["pkceMethod"] = "S256",
        };
        if (endpoints.TryGetValue("userinfo_endpoint", out var userInfo))
        {
            config["userInfoUrl"] = userInfo;
        }

        if (endpoints.TryGetValue("end_session_endpoint", out var logout))
        {
            config["logoutUrl"] = logout;
        }

        return new Dictionary<string, object?>
        {
            ["alias"] = provider.Alias,
            ["providerId"] = "oidc",
            ["enabled"] = true,
            ["trustEmail"] = false,
            ["storeToken"] = false,
            ["firstBrokerLoginFlowAlias"] = FirstLoginFlowAlias,
            ["config"] = config,
        };
    }

    /// <summary>Adds or updates the provider's mappers by name and removes every other mapper of the provider.</summary>
    private async Task SyncMappersAsync(string token, string instancePath, RealmBrokeredProvider provider, CancellationToken cancellationToken)
    {
        var desired = new Dictionary<string, (string Type, Dictionary<string, string> Config)>(StringComparer.Ordinal)
        {
            ["display name"] = AttributeMapper(provider.DisplayNameClaim, "firstName"),
            ["email"] = AttributeMapper(provider.EmailClaim, "email"),
            ["locale"] = AttributeMapper(provider.LocaleClaim, "locale"),
            // Set once, when the user is created. Keycloak lowercases the username.
            [UsernameMapperName] = (UsernameMapper, new Dictionary<string, string>
            {
                ["syncMode"] = "IMPORT",
                ["template"] = "${ALIAS}.${CLAIM.sub}",
                ["target"] = "LOCAL",
            }),
        };
        foreach (var claim in provider.RolesClaims)
        {
            desired[$"roles {claim}"] = AttributeMapper(claim, $"axis_roles_{claim}");
        }

        using var listed = await SendAsync(token, HttpMethod.Get, $"{instancePath}/mappers", null, cancellationToken);
        EnsureSuccess(listed, $"list the mappers of '{provider.Alias}'");
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        var stale = new List<(string Name, string Id)>();
        foreach (var mapper in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            var name = mapper.GetProperty("name").GetString()!;
            var id = mapper.GetProperty("id").GetString()!;
            if (desired.ContainsKey(name) && !existing.ContainsKey(name))
            {
                existing[name] = id;
            }
            else
            {
                stale.Add((name, id));
            }
        }

        foreach (var (name, (type, config)) in desired)
        {
            var representation = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["identityProviderAlias"] = provider.Alias,
                ["identityProviderMapper"] = type,
                ["config"] = config,
            };
            if (existing.TryGetValue(name, out var id))
            {
                representation["id"] = id;
                using var updated = await SendAsync(token, HttpMethod.Put, $"{instancePath}/mappers/{id}", representation, cancellationToken);
                EnsureSuccess(updated, $"update the mapper '{name}' of '{provider.Alias}'");
            }
            else
            {
                using var created = await SendAsync(token, HttpMethod.Post, $"{instancePath}/mappers", representation, cancellationToken);
                EnsureSuccess(created, $"create the mapper '{name}' of '{provider.Alias}'");
            }
        }

        foreach (var (name, id) in stale)
        {
            using var deleted = await SendAsync(token, HttpMethod.Delete, $"{instancePath}/mappers/{id}", null, cancellationToken);
            EnsureSuccess(deleted, $"delete the mapper '{name}' of '{provider.Alias}'");
        }
    }

    private static (string Type, Dictionary<string, string> Config) AttributeMapper(string claim, string attribute) =>
        (UserAttributeMapper, new Dictionary<string, string>
        {
            ["syncMode"] = "FORCE",
            ["claim"] = claim,
            ["user.attribute"] = attribute,
        });

    internal async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseAddress, path));
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
            throw new InvalidOperationException($"The identity provider step '{step}' failed with status {(int)response.StatusCode}.");
        }
    }
}
