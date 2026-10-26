using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Axis.Server.Authentication;

/// <summary>
/// The token request of a <c>fapi2</c> sign-in. It carries a DPoP proof (RFC 9449) signed with a key that is made for
/// this request and thrown away after it, so the access token the provider returns is bound to that key.
/// </summary>
internal static class DpopTokenRequest
{
    private const string NonceError = "use_dpop_nonce";

    /// <summary>
    /// Posts the token request. When the provider answers <c>use_dpop_nonce</c>, the request is sent once more with the
    /// nonce in a new proof, signed with the same key. A second refusal fails like any other error.
    /// </summary>
    /// <param name="createRequest">
    /// Makes the form of one attempt. It is called once per attempt, so each attempt gets its own client assertion.
    /// </param>
    /// <exception cref="OpenIdConnectProtocolException">The provider refused the request or answered with no JSON.</exception>
    public static async Task<OpenIdConnectMessage> SendAsync(
        HttpClient backchannel,
        string tokenEndpoint,
        Func<OpenIdConnectMessage> createRequest,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? nonce = null;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(createRequest().Parameters),
            };
            request.Headers.TryAddWithoutValidation("DPoP", DpopProof.Create(key, HttpMethod.Post.Method, tokenEndpoint, clock.GetUtcNow(), nonce));
            using var response = await backchannel.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ReadMessage(body);
            }

            var (error, description) = ReadError(body);
            if (attempt == 1
                && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                && error == NonceError
                && response.Headers.TryGetValues("DPoP-Nonce", out var nonces)
                && nonces.FirstOrDefault() is { Length: > 0 } offered)
            {
                nonce = offered;
                continue;
            }

            throw new OpenIdConnectProtocolException(
                $"The token endpoint refused the code: {(int)response.StatusCode} error '{error}', description '{description}'.");
        }
    }

    private static OpenIdConnectMessage ReadMessage(string body)
    {
        try
        {
            return new OpenIdConnectMessage(body);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new OpenIdConnectProtocolException("The token endpoint answered with no valid JSON.", exception);
        }
    }

    private static (string? Error, string? Description) ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (ReadString(document.RootElement, "error"), ReadString(document.RootElement, "error_description"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
