using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Trades a person's platform access token for the agent host's token at a tenant's realm, and refreshes that token, see
/// <c>docs/reference/authoring-requests.md#acting-for-the-author</c>. It signs in as <see cref="RealmAgentClient.ClientId"/>. The exchange
/// uses standard token exchange, names the scope <see cref="RealmSync.AuthoringScope"/> and no audience, because the scope's mappers put the
/// MCP URL into the token, and a refused exchange is any answer of 400. A refresh counts as refused only for a 400 with the error
/// <c>invalid_grant</c>, as the server's session refresh does. Any other failure throws, and the error names the step and the status code,
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

    /// <summary>How long the agent host waits for a refresh, so a realm that does not answer does not hold the step.</summary>
    public static readonly TimeSpan RefreshLimit = TimeSpan.FromSeconds(10);

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
        using var response = await http.PostAsync(TokenEndpoint(), content, cancellationToken);
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

    /// <summary>
    /// Refreshes the agent host's token. The answer holds the new access token and the refresh token to keep: the one the realm rotated to, or
    /// the given one when the realm returns none. A 400 with the error <c>invalid_grant</c> answers <see cref="RealmAgentExchange.Refused"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The realm answered any other status, or a 200 without an access token.</exception>
    public async Task<RealmAgentExchange> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        const string Step = "refresh the agent host's token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = RealmAgentClient.ClientId,
            ["client_secret"] = access.ClientSecret.Reveal(),
            ["refresh_token"] = refreshToken,
        });
        using var response = await http.PostAsync(TokenEndpoint(), content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var error = await ReadBodyAsync(response, cancellationToken);
            if (Text(error, "error") == "invalid_grant")
            {
                return RealmAgentExchange.Refused;
            }

            throw new InvalidOperationException($"The identity provider step '{Step}' failed with status {(int)response.StatusCode}.");
        }

        RealmSync.EnsureSuccess(response, Step);
        var body = await ReadBodyAsync(response, cancellationToken);
        return Text(body, "access_token") is { } accessToken
            ? new RealmAgentExchange(accessToken, Text(body, "refresh_token") ?? refreshToken)
            : throw new InvalidOperationException($"The identity provider step '{Step}' answered without an access token.");
    }

    private Uri TokenEndpoint() =>
        new(access.BaseAddress, $"realms/{Uri.EscapeDataString(access.Realm)}/protocol/openid-connect/token");

    // A body that is not JSON reads as nothing, so the caller decides by the status.
    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
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
/// The answer of a token exchange or a refresh: the agent host's access token and refresh token, or both <c>null</c> when the realm refused the grant.
/// </summary>
public sealed record RealmAgentExchange(string? AccessToken, string? RefreshToken)
{
    /// <summary>The realm refused the grant, for example because the person's realm session ended.</summary>
    public static RealmAgentExchange Refused { get; } = new(null, null);

    /// <summary>True when the realm refused the grant.</summary>
    public bool IsRefused => AccessToken is null;
}
