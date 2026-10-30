using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Realms;
using Axis.Configuration.Resources;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Makes a tenant's realm hold the site clients and brokered <c>oidc</c> and <c>saml</c> providers of a <see cref="RealmState"/>. It
/// signs in to the realm as <c>axis-activation</c> with the client credentials grant, so it needs no rights in the
/// master realm. Each site gets a confidential client <c>site-</c> and its path, which carries the attributes <c>axis.providers</c> (the aliases of the providers the site allows, separated by spaces) and <c>axis.accounts</c> (<c>true</c> or <c>false</c>), and which copies the redirect addresses, web origins and
/// post-logout redirect addresses of <c>axis</c> and, when the state holds the platform secret, has the secret that
/// <see cref="SiteClientSecret"/> derives from it. Its ID token carries the alias of the
/// provider that signed the person in as the <c>identity_provider</c> claim, and the <c>acr</c> that the provider signed the person in at as the
/// <c>identity_provider_acr</c> claim, when the provider sent one. Each site client also copies the protocol mappers of
/// <c>axis</c> by name, except the two named <c>identity provider</c> and <c>identity provider acr</c>, and never removes a mapper. Each
/// provider gets claim mappers for the display name, the email, the locale and each roles claim, an <c>acr</c> mapper that copies the
/// provider's <c>acr</c> claim into the person's realm session, and a <c>username</c> mapper that names a new user after the
/// provider's alias and the person's subject. A <c>saml</c> provider is imported from its metadata, which the sync reads
/// before the realm changes and uploads to the import-config endpoint of the realm. It asks for a persistent NameID, signs its requests with
/// the realm's own key (RSA-SHA256), and gets <c>saml-user-attribute-idp-mapper</c> mappers for the same claims as an <c>oidc</c> provider, no <c>acr</c> mapper,
/// and a <c>username</c> mapper that names a new user after the provider's alias and the NameID. A provider with labels has the display name <c>${axisProvider.&lt;alias&gt;}</c>, and the
/// sync writes the label of each platform locale as the realm text of that key through the realm localization API, which
/// needs the <c>manage-realm</c> right, and removes the label texts (<see cref="LabelKeyPrefix"/>) of providers the realm no longer holds. Each provider also names the realm's own first-login flow
/// (<see cref="FirstLoginFlowAlias"/>), which always creates a new user and never offers to link one. The sync adds, changes and removes only clients whose id
/// starts with <see cref="SiteClientPrefix"/> and providers whose alias starts with <see cref="ProviderAliasPrefix"/> or <see cref="SamlProviderAliasPrefix"/>,
/// so <c>axis</c>, <c>axis-activation</c>, <c>axis-users</c> and anything an operator added stay. A removed provider
/// keeps its users in the realm. An error names the step and the status code, never a response body or a secret. A 409
/// from the identity provider, which another sync of the same realm at the same time can cause, makes the sync read the
/// realm again and repeat, up to three attempts.
/// </summary>
public sealed class RealmSync(HttpClient http, Uri baseAddress)
{
    public const string SiteClientPrefix = "site-";
    public const string ProviderAliasPrefix = "oidc-";

    /// <summary>The start of the alias of a brokered <c>saml</c> provider.</summary>
    public const string SamlProviderAliasPrefix = "saml-";
    public const string IdentityProviderClaim = "identity_provider";

    /// <summary>The claim of a site client's ID token that holds the <c>acr</c> the brokered provider signed the person in at.</summary>
    public const string IdentityProviderAcrClaim = "identity_provider_acr";

    /// <summary>The start of the realm message key that holds a provider's label.</summary>
    public const string LabelKeyPrefix = "axisProvider.";

    /// <summary>The realm message key that holds the label of the provider with <paramref name="alias"/>.</summary>
    public static string LabelKey(string alias) => LabelKeyPrefix + alias;

    /// <summary>The user attribute that holds the values of one roles claim of a brokered provider.</summary>
    public static string RolesAttribute(string claim) => $"axis_roles_{claim}";

    /// <summary>The attribute of a site client that holds the aliases of the providers the site allows, separated by spaces.</summary>
    public const string ProvidersAttribute = "axis.providers";

    /// <summary>The attribute of a site client that holds <c>true</c> when the site allows accounts and <c>false</c> when it does not.</summary>
    public const string AccountsAttribute = "axis.accounts";

    private const string PostLogoutRedirectUrisAttribute = "post.logout.redirect.uris";

    /// <summary>The alias of the first-login flow that <c>TenantRealm</c> makes and every provider names.</summary>
    public const string FirstLoginFlowAlias = "axis first login";

    private const int ConflictAttempts = 3;
    private const string ActivationClientId = "axis-activation";
    private const string UserAttributeMapper = "oidc-user-attribute-idp-mapper";
    private const string UsernameMapper = "oidc-username-idp-mapper";
    private const string SamlUserAttributeMapper = "saml-user-attribute-idp-mapper";
    private const string SamlUsernameMapper = "saml-username-idp-mapper";
    private const string UsernameMapperName = "username";
    private const string SessionNoteMapper = "oidc-usersessionmodel-note-mapper";
    private const string SessionNoteMapperName = "identity provider";
    private const string AcrSessionNoteMapperName = "identity provider acr";
    private const string AcrNoteMapper = "oidc-user-session-note-idp-mapper";
    private const string AcrMapperName = "acr";
    private const string AcrNote = "acr";

    public static string SiteClientId(string path) => SiteClientPrefix + path;

    public async Task SyncAsync(string realm, string activationSecret, RealmState state, CancellationToken cancellationToken)
    {
        Validate(state);

        var token = await ActivationTokenAsync(realm, activationSecret, cancellationToken);
        var realmPath = $"admin/realms/{Uri.EscapeDataString(realm)}";

        // Every discovery document and every metadata document is read before the realm changes, so an unreachable issuer
        // or metadata address changes nothing.
        var providers = new List<ProviderSource>();
        foreach (var provider in state.Providers)
        {
            providers.Add(provider.Metadata is { } metadata
                ? new ProviderSource(provider, [], await ReadMetadataAsync(provider.Alias, metadata, cancellationToken))
                : new ProviderSource(provider, await ReadDiscoveryAsync(provider, cancellationToken), null));
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await SyncSiteClientsAsync(token, realmPath, state.Sites, state.PlatformClientSecret, cancellationToken);
                await SyncProvidersAsync(token, realmPath, providers, cancellationToken);
                await SyncLabelsAsync(token, realmPath, state.Providers, cancellationToken);
                return;
            }
            catch (ConflictException) when (attempt < ConflictAttempts)
            {
                // Another sync of the same realm wrote at the same moment. Read the realm again, so a create that
                // lost the race turns into an update, and wait a little longer each time with some jitter.
                await Task.Delay(TimeSpan.FromMilliseconds((200 * attempt) + Random.Shared.Next(0, 200)), cancellationToken);
            }
        }
    }

    private static void Validate(RealmState state)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in state.Sites)
        {
            if (!paths.Add(site.Path))
            {
                throw new ArgumentException($"The site path '{site.Path}' appears twice.", nameof(state));
            }

            foreach (var alias in site.Providers)
            {
                if (alias.Length == 0 || alias.Any(char.IsWhiteSpace))
                {
                    throw new ArgumentException($"The provider alias '{alias}' of the site '{site.Path}' is empty or contains whitespace.", nameof(state));
                }
            }
        }

        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in state.Providers)
        {
            var prefix = provider.Metadata is null ? ProviderAliasPrefix : SamlProviderAliasPrefix;
            if (!provider.Alias.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException($"The provider alias '{provider.Alias}' does not start with '{prefix}'.", nameof(state));
            }

            if (provider.Metadata is null && (provider.Issuer is null || provider.ClientId is null || provider.ClientSecret is null))
            {
                throw new ArgumentException($"The provider '{provider.Alias}' has no issuer, client id or client secret.", nameof(state));
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

    private async Task SyncSiteClientsAsync(string token, string realmPath, IReadOnlyList<RealmSiteClient> sites, string? platformSecret, CancellationToken cancellationToken)
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

        var platformMappers = PlatformMappers(platformClient);
        var desired = sites.ToDictionary(site => SiteClientId(site.Path), StringComparer.Ordinal);
        foreach (var (clientId, site) in desired)
        {
            string uuid;
            if (!existing.TryGetValue(clientId, out var current))
            {
                using var created = await SendAsync(token, HttpMethod.Post, $"{realmPath}/clients", SiteClient(clientId, site, platformClient, null, platformSecret), cancellationToken);
                EnsureSuccess(created, $"create the client '{clientId}'");
                uuid = created.Headers.Location?.Segments[^1].TrimEnd('/')
                    ?? throw new InvalidOperationException($"The identity provider step 'find the created client '{clientId}'' found no client.");
            }
            else
            {
                uuid = current.Uuid;
                using var updated = await SendAsync(token, HttpMethod.Put, $"{realmPath}/clients/{uuid}", SiteClient(clientId, site, platformClient, current.Representation, platformSecret), cancellationToken);
                EnsureSuccess(updated, $"update the client '{clientId}'");
            }

            await EnsureSessionNoteMapperAsync(token, realmPath, uuid, clientId, SessionNoteMapperName, SessionNoteConfig(IdentityProviderClaim, IdentityProviderClaim), cancellationToken);
            await EnsureSessionNoteMapperAsync(token, realmPath, uuid, clientId, AcrSessionNoteMapperName, SessionNoteConfig(AcrNote, IdentityProviderAcrClaim), cancellationToken);
            await EnsurePlatformMappersAsync(token, realmPath, uuid, clientId, platformMappers, cancellationToken);
        }

        foreach (var (clientId, (uuid, _)) in existing.Where(pair => !desired.ContainsKey(pair.Key)))
        {
            using var deleted = await SendAsync(token, HttpMethod.Delete, $"{realmPath}/clients/{uuid}", null, cancellationToken);
            EnsureSuccess(deleted, $"delete the client '{clientId}'");
        }
    }

    /// <summary>
    /// When <paramref name="platformSecret"/> is set, the secret is <see cref="SiteClientSecret.Derive"/> of it. Otherwise the secret is
    /// left out, so Keycloak makes one when it creates the client and keeps it on an update. When the realm has the platform client
    /// <c>axis</c>, the site client takes its redirect addresses, web origins and post-logout redirect addresses, its client
    /// authentication and the settings of its sign-in profile and keys (<see cref="RealmClientProfile"/>), so activation never undoes
    /// what <c>tenant oidc set</c> wrote. An update keeps the other attributes of the existing client, because Keycloak removes the
    /// attributes that a client update leaves out. The attributes <see cref="ProvidersAttribute"/> and <see cref="AccountsAttribute"/> hold
    /// the aliases and the accounts flag of <paramref name="site"/>, which the realm's sign-in page reads.
    /// </summary>
    private static Dictionary<string, object?> SiteClient(string clientId, RealmSiteClient site, JsonElement? platformClient, JsonElement? existing, string? platformSecret)
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
        if (platformSecret is not null)
        {
            client["secret"] = SiteClientSecret.Derive(platformSecret, clientId);
        }

        Dictionary<string, string> attributes;
        if (platformClient is { } platform)
        {
            client["clientAuthenticatorType"] = RealmClientProfile.AuthenticatorTypeOf(platform);
            client["redirectUris"] = StringArray(platform, "redirectUris");
            client["webOrigins"] = StringArray(platform, "webOrigins");
            attributes = RealmClientProfile.AttributesFor(platform, existing);
            attributes[PostLogoutRedirectUrisAttribute] = RealmClientProfile.AttributeOf(platform, PostLogoutRedirectUrisAttribute) ?? "";
        }
        else
        {
            attributes = existing is { } current
                ? RealmClientProfile.ReadAttributes(current)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        attributes[ProvidersAttribute] = string.Join(' ', site.Providers);
        attributes[AccountsAttribute] = site.Accounts ? "true" : "false";
        client["attributes"] = attributes;
        return client;
    }

    private static string[] StringArray(JsonElement client, string name) =>
        client.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    private static Dictionary<string, string> SessionNoteConfig(string note, string claim) => new()
    {
        ["user.session.note"] = note,
        ["claim.name"] = claim,
        ["id.token.claim"] = "true",
        ["access.token.claim"] = "false",
        ["userinfo.token.claim"] = "false",
        ["jsonType.label"] = "String",
    };

    /// <summary>A client update does not replace protocol mappers, so this adds the mapper or corrects its config.</summary>
    private async Task EnsureSessionNoteMapperAsync(
        string token,
        string realmPath,
        string uuid,
        string clientId,
        string name,
        Dictionary<string, string> desired,
        CancellationToken cancellationToken)
    {
        var mappersPath = $"{realmPath}/clients/{uuid}/protocol-mappers/models";
        using var listed = await SendAsync(token, HttpMethod.Get, mappersPath, null, cancellationToken);
        EnsureSuccess(listed, $"list the mappers of the client '{clientId}'");
        foreach (var mapper in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            if (mapper.GetProperty("name").GetString() != name)
            {
                continue;
            }

            if (mapper.GetProperty("protocolMapper").GetString() == SessionNoteMapper && ConfigMatches(mapper, desired))
            {
                return;
            }

            var representation = SessionNoteMapperRepresentation(name, desired);
            representation["id"] = mapper.GetProperty("id").GetString();
            using var updated = await SendAsync(
                token, HttpMethod.Put, $"{mappersPath}/{representation["id"]}", representation, cancellationToken);
            EnsureSuccess(updated, $"update the mapper '{name}' of the client '{clientId}'");
            return;
        }

        using var created = await SendAsync(token, HttpMethod.Post, mappersPath, SessionNoteMapperRepresentation(name, desired), cancellationToken);
        EnsureSuccess(created, $"create the mapper '{name}' of the client '{clientId}'");
    }

    private static Dictionary<string, object?> SessionNoteMapperRepresentation(string name, Dictionary<string, string> config) => new()
    {
        ["name"] = name,
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

    /// <summary>The protocol mappers of the platform client except the one the sync keeps as it is, or none without the client.</summary>
    private static List<(string Name, string Protocol, string Type, Dictionary<string, string> Config)> PlatformMappers(JsonElement? platformClient)
    {
        var mappers = new List<(string Name, string Protocol, string Type, Dictionary<string, string> Config)>();
        if (platformClient is not { } platform
            || !platform.TryGetProperty("protocolMappers", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return mappers;
        }

        foreach (var mapper in array.EnumerateArray())
        {
            var name = mapper.GetProperty("name").GetString()!;
            if (name is SessionNoteMapperName or AcrSessionNoteMapperName)
            {
                continue;
            }

            var protocol = mapper.TryGetProperty("protocol", out var protocolValue) && protocolValue.ValueKind == JsonValueKind.String
                ? protocolValue.GetString()!
                : "openid-connect";
            var config = new Dictionary<string, string>(StringComparer.Ordinal);
            if (mapper.TryGetProperty("config", out var configValue) && configValue.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in configValue.EnumerateObject())
                {
                    config[entry.Name] = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : entry.Value.GetRawText();
                }
            }

            mappers.Add((name, protocol, mapper.GetProperty("protocolMapper").GetString()!, config));
        }

        return mappers;
    }

    /// <summary>
    /// A client update does not replace protocol mappers, so this creates each mapper of <paramref name="platformMappers"/> that the
    /// client lacks and updates one whose type or config differs. It never removes a mapper.
    /// </summary>
    private async Task EnsurePlatformMappersAsync(
        string token,
        string realmPath,
        string uuid,
        string clientId,
        List<(string Name, string Protocol, string Type, Dictionary<string, string> Config)> platformMappers,
        CancellationToken cancellationToken)
    {
        if (platformMappers.Count == 0)
        {
            return;
        }

        var mappersPath = $"{realmPath}/clients/{uuid}/protocol-mappers/models";
        using var listed = await SendAsync(token, HttpMethod.Get, mappersPath, null, cancellationToken);
        EnsureSuccess(listed, $"list the mappers of the client '{clientId}'");
        var existing = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var mapper in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            existing.TryAdd(mapper.GetProperty("name").GetString()!, mapper);
        }

        foreach (var (name, protocol, type, config) in platformMappers)
        {
            var representation = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["protocol"] = protocol,
                ["protocolMapper"] = type,
                ["config"] = config,
            };
            if (!existing.TryGetValue(name, out var current))
            {
                using var created = await SendAsync(token, HttpMethod.Post, mappersPath, representation, cancellationToken);
                EnsureSuccess(created, $"create the mapper '{name}' of the client '{clientId}'");
            }
            else if (current.GetProperty("protocolMapper").GetString() != type || !ConfigEquals(current, config))
            {
                representation["id"] = current.GetProperty("id").GetString();
                using var updated = await SendAsync(token, HttpMethod.Put, $"{mappersPath}/{representation["id"]}", representation, cancellationToken);
                EnsureSuccess(updated, $"update the mapper '{name}' of the client '{clientId}'");
            }
        }
    }

    private static bool ConfigEquals(JsonElement mapper, Dictionary<string, string> desired)
    {
        var count = 0;
        if (mapper.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in config.EnumerateObject())
            {
                var value = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : entry.Value.GetRawText();
                if (!desired.TryGetValue(entry.Name, out var expected) || expected != value)
                {
                    return false;
                }

                count++;
            }
        }

        return count == desired.Count;
    }

    private async Task<string> ReadMetadataAsync(string alias, Uri address, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(address, cancellationToken);
        EnsureSuccess(response, $"read the metadata of '{alias}'");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<Dictionary<string, string>> ReadDiscoveryAsync(RealmBrokeredProvider provider, CancellationToken cancellationToken)
    {
        var step = $"read the discovery document of '{provider.Alias}'";
        var address = new Uri(provider.Issuer!.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
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
        List<ProviderSource> providers,
        CancellationToken cancellationToken)
    {
        using var listed = await SendAsync(token, HttpMethod.Get, $"{realmPath}/identity-provider/instances", null, cancellationToken);
        EnsureSuccess(listed, "list the identity providers");
        var existing = new List<string>();
        foreach (var instance in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            var alias = instance.GetProperty("alias").GetString()!;
            if (alias.StartsWith(ProviderAliasPrefix, StringComparison.Ordinal) || alias.StartsWith(SamlProviderAliasPrefix, StringComparison.Ordinal))
            {
                existing.Add(alias);
            }
        }

        foreach (var (provider, endpoints, metadata) in providers)
        {
            var instancePath = $"{realmPath}/identity-provider/instances/{Uri.EscapeDataString(provider.Alias)}";
            var representation = metadata is null
                ? ProviderRepresentation(provider, endpoints)
                : SamlProviderRepresentation(provider, await ImportMetadataAsync(token, realmPath, provider.Alias, metadata, cancellationToken));
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

    /// <summary>
    /// Writes the label of each provider as a realm text of each platform locale when the stored text differs, and removes the
    /// texts with a key that starts with <see cref="LabelKeyPrefix"/> and that no provider wants.
    /// </summary>
    private async Task SyncLabelsAsync(string token, string realmPath, IReadOnlyList<RealmBrokeredProvider> providers, CancellationToken cancellationToken)
    {
        foreach (var locale in PlatformLocales.All)
        {
            var localePath = $"{realmPath}/localization/{Uri.EscapeDataString(locale)}";
            using var listed = await SendAsync(token, HttpMethod.Get, localePath, null, cancellationToken);
            EnsureSuccess(listed, $"list the texts in '{locale}'");
            var stored = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateObject())
            {
                if (property.Name.StartsWith(LabelKeyPrefix, StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String)
                {
                    stored[property.Name] = property.Value.GetString()!;
                }
            }

            var desired = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var provider in providers)
            {
                if (provider.Labels is { Count: > 0 } labels && labels.TryGetValue(locale, out var label))
                {
                    desired[LabelKey(provider.Alias)] = label;
                }
            }

            foreach (var (key, label) in desired)
            {
                if (stored.TryGetValue(key, out var current) && current == label)
                {
                    continue;
                }

                using var written = await SendAsync(
                    token, HttpMethod.Put, $"{localePath}/{Uri.EscapeDataString(key)}", label, cancellationToken);
                EnsureSuccess(written, $"write the label of '{key[LabelKeyPrefix.Length..]}' in '{locale}'");
            }

            foreach (var key in stored.Keys.Where(key => !desired.ContainsKey(key)))
            {
                using var deleted = await SendAsync(token, HttpMethod.Delete, $"{localePath}/{Uri.EscapeDataString(key)}", null, cancellationToken);
                EnsureSuccess(deleted, $"delete the label of '{key[LabelKeyPrefix.Length..]}' in '{locale}'");
            }
        }
    }

    /// <summary>Has Keycloak read the SAML metadata and return the provider config that it makes of it.</summary>
    private async Task<Dictionary<string, string>> ImportMetadataAsync(
        string token, string realmPath, string alias, string metadata, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("saml"), "providerId" },
            { new StringContent(metadata, Encoding.UTF8, "application/xml"), "file", "metadata.xml" },
        };
        using var response = await SendAsync(token, HttpMethod.Post, $"{realmPath}/identity-provider/import-config", content, cancellationToken);
        EnsureSuccess(response, $"import the metadata of '{alias}'");
        return await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken)
            ?? throw new InvalidOperationException($"The identity provider step 'import the metadata of '{alias}'' found no config.");
    }

    private static Dictionary<string, object?> SamlProviderRepresentation(RealmBrokeredProvider provider, Dictionary<string, string> imported)
    {
        var config = new Dictionary<string, string>(imported, StringComparer.Ordinal)
        {
            ["wantAuthnRequestsSigned"] = "true",
            ["signatureAlgorithm"] = "RSA_SHA256",
            ["xmlSigKeyInfoKeyNameTransformer"] = "KEY_ID",
            ["nameIDPolicyFormat"] = "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent",
            ["principalType"] = "SUBJECT",
            ["syncMode"] = "FORCE",
        };
        var representation = new Dictionary<string, object?>
        {
            ["alias"] = provider.Alias,
            ["providerId"] = "saml",
            ["enabled"] = true,
            ["trustEmail"] = false,
            ["storeToken"] = false,
            ["firstBrokerLoginFlowAlias"] = FirstLoginFlowAlias,
            ["config"] = config,
        };
        if (provider.Labels is { Count: > 0 })
        {
            representation["displayName"] = "${" + LabelKey(provider.Alias) + "}";
        }

        return representation;
    }

    private static Dictionary<string, object?> ProviderRepresentation(RealmBrokeredProvider provider, Dictionary<string, string> endpoints)
    {
        var config = new Dictionary<string, string>
        {
            ["issuer"] = provider.Issuer!.AbsoluteUri,
            ["authorizationUrl"] = endpoints["authorization_endpoint"],
            ["tokenUrl"] = endpoints["token_endpoint"],
            ["jwksUrl"] = endpoints["jwks_uri"],
            ["useJwksUrl"] = "true",
            ["validateSignature"] = "true",
            ["clientId"] = provider.ClientId!,
            ["clientSecret"] = provider.ClientSecret!,
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

        var representation = new Dictionary<string, object?>
        {
            ["alias"] = provider.Alias,
            ["providerId"] = "oidc",
            ["enabled"] = true,
            ["trustEmail"] = false,
            ["storeToken"] = false,
            ["firstBrokerLoginFlowAlias"] = FirstLoginFlowAlias,
            ["config"] = config,
        };
        if (provider.Labels is { Count: > 0 })
        {
            representation["displayName"] = "${" + LabelKey(provider.Alias) + "}";
        }

        return representation;
    }

    /// <summary>Adds or updates the provider's mappers by name and removes every other mapper of the provider.</summary>
    private async Task SyncMappersAsync(string token, string instancePath, RealmBrokeredProvider provider, CancellationToken cancellationToken)
    {
        var saml = provider.Metadata is not null;
        (string Type, Dictionary<string, string> Config) Attribute(string claim, string attribute) =>
            saml ? SamlAttributeMapper(claim, attribute) : AttributeMapper(claim, attribute);

        var desired = new Dictionary<string, (string Type, Dictionary<string, string> Config)>(StringComparer.Ordinal)
        {
            ["display name"] = Attribute(provider.DisplayNameClaim, "firstName"),
            ["email"] = Attribute(provider.EmailClaim, "email"),
            ["locale"] = Attribute(provider.LocaleClaim, "locale"),
            // Set once, when the user is created. Keycloak lowercases the username.
            [UsernameMapperName] = saml
                ? (SamlUsernameMapper, new Dictionary<string, string>
                {
                    ["syncMode"] = "IMPORT",
                    ["template"] = "${ALIAS}.${NAMEID}",
                    ["target"] = "LOCAL",
                })
                : (UsernameMapper, new Dictionary<string, string>
                {
                    ["syncMode"] = "IMPORT",
                    ["template"] = "${ALIAS}.${CLAIM.sub}",
                    ["target"] = "LOCAL",
                }),
        };
        if (!saml)
        {
            // Copies the upstream acr into the session note "acr", whatever its value, for the site client's acr mapper to read.
            desired[AcrMapperName] = (AcrNoteMapper, new Dictionary<string, string>
            {
                ["syncMode"] = "FORCE",
                ["claims"] = """[{"key":"acr","value":".*"}]""",
                ["are.claim.values.regex"] = "true",
            });
        }

        foreach (var claim in provider.RolesClaims)
        {
            desired[$"roles {claim}"] = Attribute(claim, RolesAttribute(claim));
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

    private static (string Type, Dictionary<string, string> Config) SamlAttributeMapper(string claim, string attribute) =>
        (SamlUserAttributeMapper, new Dictionary<string, string>
        {
            ["syncMode"] = "FORCE",
            ["attribute.name"] = claim,
            ["user.attribute"] = attribute,
        });

    internal async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is string text)
        {
            request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
        }
        else if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
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
            throw response.StatusCode == HttpStatusCode.Conflict
                ? new ConflictException(message)
                : new InvalidOperationException(message);
        }
    }

    private sealed record ProviderSource(RealmBrokeredProvider Provider, Dictionary<string, string> Endpoints, string? Metadata);

    private sealed class ConflictException(string message) : InvalidOperationException(message);
}
