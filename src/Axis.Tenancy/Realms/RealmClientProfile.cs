using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Tenancy.Realms;

/// <summary>The public part of a tenant's EC P-256 client key: its key id (the RFC 7638 thumbprint) and the base64url <paramref name="X"/> and <paramref name="Y"/>.</summary>
public sealed record RealmClientKey(string KeyId, string X, string Y);

/// <summary>
/// Makes the platform client <c>axis</c> and every site client of a tenant's realm follow the tenant's sign-in profile and
/// client keys. Under <c>fapi2</c> a client authenticates with a signed client assertion (<c>private_key_jwt</c>, ES256) and
/// requires PAR, PKCE <c>S256</c> and DPoP-bound tokens. Under <c>standard</c> it goes back to the client secret and requires
/// neither PAR nor DPoP. In both profiles the client lists every key that the tenant holds. It signs in to the realm as
/// <c>axis-activation</c> and writes each client whole after it has read it, so redirect addresses, secrets and mappers stay.
/// An error names the step and the status code, never a response body or a secret.
/// </summary>
public sealed class RealmClientProfile(HttpClient http, Uri baseAddress)
{
    /// <summary>The client id of the platform client that the tenant's server signs in with.</summary>
    public const string PlatformClientId = "axis";

    private const string ClientSecretAuthenticator = "client-secret";
    private const string ClientJwtAuthenticator = "client-jwt";

    private static readonly string[] _attributeNames =
    [
        "require.pushed.authorization.requests",
        "pkce.code.challenge.method",
        "token.endpoint.auth.signing.alg",
        "dpop.bound.access.tokens",
        "use.jwks.url",
        "use.jwks.string",
        "jwks.string",
    ];

    private readonly RealmSync _sync = new(http, baseAddress);

    /// <summary>The names of the client attributes that carry the profile and the keys.</summary>
    public static IReadOnlyList<string> AttributeNames => _attributeNames;

    /// <summary>The client authentication a client uses under <paramref name="profile"/>.</summary>
    public static string AuthenticatorType(string profile) =>
        profile == TenantSignInProfiles.Fapi2 ? ClientJwtAuthenticator : ClientSecretAuthenticator;

    /// <summary>
    /// The client attributes that carry the profile and the keys. A client update does not remove an attribute that it leaves out,
    /// so a writer sets every attribute of <see cref="AttributeNames"/> that is not in the result to an empty value, which
    /// Keycloak stores as no value. A <c>standard</c> client then has neither a PKCE method nor a signing algorithm.
    /// </summary>
    public static Dictionary<string, string> Attributes(string profile, IReadOnlyList<RealmClientKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var fapi2 = profile == TenantSignInProfiles.Fapi2;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["require.pushed.authorization.requests"] = fapi2 ? "true" : "false",
            ["dpop.bound.access.tokens"] = fapi2 ? "true" : "false",
            ["use.jwks.url"] = "false",
            ["use.jwks.string"] = "true",
            ["jwks.string"] = Jwks(keys),
        };
        if (fapi2)
        {
            attributes["pkce.code.challenge.method"] = "S256";
            attributes["token.endpoint.auth.signing.alg"] = "ES256";
        }

        return attributes;
    }

    /// <summary>The client authentication of a client representation, or the client secret when it names none.</summary>
    internal static string AuthenticatorTypeOf(JsonElement client) =>
        client.TryGetProperty("clientAuthenticatorType", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()!
            : ClientSecretAuthenticator;

    /// <summary>
    /// The attributes of a site client: the ones of <paramref name="existing"/> except the profile and key attributes, which are
    /// the ones of the platform client. A profile attribute that the platform client lacks is set empty.
    /// </summary>
    internal static Dictionary<string, string> AttributesFor(JsonElement platformClient, JsonElement? existing)
    {
        var attributes = existing is { } current ? ReadAttributes(current) : new Dictionary<string, string>(StringComparer.Ordinal);
        var platform = ReadAttributes(platformClient);
        foreach (var name in _attributeNames)
        {
            attributes[name] = platform.GetValueOrDefault(name, "");
        }

        return attributes;
    }

    /// <summary>
    /// Writes the profile and the keys on <c>axis</c> and on every <c>site-</c> client of the realm. A client that the realm
    /// does not hold is skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException">A step failed. The message names the step and the status code.</exception>
    public async Task ApplyAsync(
        string realm,
        string activationSecret,
        string profile,
        IReadOnlyList<RealmClientKey> keys,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realm);
        ArgumentNullException.ThrowIfNull(keys);

        var token = await _sync.ActivationTokenAsync(realm, activationSecret, cancellationToken);
        var clientsPath = $"admin/realms/{Uri.EscapeDataString(realm)}/clients";
        using var listed = await _sync.SendAsync(token, HttpMethod.Get, $"{clientsPath}?max=1000", null, cancellationToken);
        RealmSync.EnsureSuccess(listed, "list the clients");
        var targets = new List<(string ClientId, string Uuid)>();
        foreach (var client in (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            var clientId = client.GetProperty("clientId").GetString()!;
            if (clientId == PlatformClientId || clientId.StartsWith(RealmSync.SiteClientPrefix, StringComparison.Ordinal))
            {
                targets.Add((clientId, client.GetProperty("id").GetString()!));
            }
        }

        var authenticator = AuthenticatorType(profile);
        var attributes = Attributes(profile, keys);
        foreach (var (clientId, uuid) in targets)
        {
            using var read = await _sync.SendAsync(token, HttpMethod.Get, $"{clientsPath}/{uuid}", null, cancellationToken);
            RealmSync.EnsureSuccess(read, $"read the client '{clientId}'");
            var representation = JsonNode.Parse(await read.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            representation["clientAuthenticatorType"] = authenticator;
            var current = representation["attributes"] as JsonObject ?? [];
            foreach (var name in _attributeNames)
            {
                current[name] = attributes.GetValueOrDefault(name, "");
            }

            representation["attributes"] = current;
            using var updated = await _sync.SendAsync(token, HttpMethod.Put, $"{clientsPath}/{uuid}", representation, cancellationToken);
            RealmSync.EnsureSuccess(updated, $"update the client '{clientId}'");
        }
    }

    /// <summary>The value of one attribute of a client representation, or <c>null</c> when it has none.</summary>
    internal static string? AttributeOf(JsonElement client, string name) => ReadAttributes(client).GetValueOrDefault(name);

    private static Dictionary<string, string> ReadAttributes(JsonElement client)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (client.TryGetProperty("attributes", out var element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var attribute in element.EnumerateObject())
            {
                if (attribute.Value.ValueKind == JsonValueKind.String)
                {
                    attributes[attribute.Name] = attribute.Value.GetString()!;
                }
            }
        }

        return attributes;
    }

    private static string Jwks(IReadOnlyList<RealmClientKey> keys)
    {
        var list = new JsonArray();
        foreach (var key in keys)
        {
            list.Add(new JsonObject
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = key.X,
                ["y"] = key.Y,
                ["kid"] = key.KeyId,
                ["use"] = "sig",
                ["alg"] = "ES256",
            });
        }

        return new JsonObject { ["keys"] = list }.ToJsonString();
    }
}
