using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Trades a person's platform access token for the agent host's token at a tenant's realm, see
/// <c>docs/reference/authoring-requests.md#acting-for-the-author</c>. It signs in as <see cref="RealmAgentClient.ClientId"/> with standard
/// token exchange, names the scope <see cref="RealmSync.AuthoringScope"/> and no audience, because the scope's mappers put the MCP URL into
/// the token. Only an answer of 400 counts as a refused grant. Any other failure throws, and the error names the step and the status code,
/// never a response body or a secret.
/// </summary>
public sealed class RealmAgentTokens(HttpClient http, TenantRealmAgent access)
{
    /// <summary>The grant type of standard token exchange.</summary>
    public const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>The token type of an access token.</summary>
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    /// <summary>The token type of a refresh token.</summary>
    public const string RefreshTokenType = "urn:ietf:params:oauth:token-type:refresh_token";

    /// <summary>
    /// Exchanges the person's access token for an access token and a refresh token of the agent host's client.
    /// </summary>
    /// <exception cref="InvalidOperationException">The realm answered a status other than 200 and 400, or a 200 without both tokens.</exception>
    public async Task<RealmAgentExchange> ExchangeAsync(string subjectToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subjectToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = TokenExchangeGrant,
            ["client_id"] = RealmAgentClient.ClientId,
            ["client_secret"] = access.ClientSecret.Reveal(),
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = AccessTokenType,
            ["requested_token_type"] = RefreshTokenType,
            ["scope"] = RealmSync.AuthoringScope,
        });
        using var response = await http.PostAsync(
            new Uri(access.BaseAddress, $"realms/{Uri.EscapeDataString(access.Realm)}/protocol/openid-connect/token"), content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return RealmAgentExchange.Refused;
        }

        RealmSync.EnsureSuccess(response, "exchange the token for the agent host");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (Text(body, "access_token") is { } accessToken && Text(body, "refresh_token") is { } refreshToken)
        {
            return new RealmAgentExchange(accessToken, refreshToken);
        }

        throw new InvalidOperationException("The identity provider step 'exchange the token for the agent host' answered without both tokens.");
    }

    private static string? Text(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
                ? text
                : null;
}

/// <summary>
/// The answer of a token exchange: the agent host's access token and refresh token, or both <c>null</c> when the realm refused the grant.
/// </summary>
public sealed record RealmAgentExchange(string? AccessToken, string? RefreshToken)
{
    /// <summary>The realm refused the grant, for example because the person's realm session ended.</summary>
    public static RealmAgentExchange Refused { get; } = new(null, null);

    /// <summary>True when the realm refused the grant.</summary>
    public bool IsRefused => AccessToken is null;
}
