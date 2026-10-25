using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Authentication;

/// <summary>
/// The DPoP proof (RFC 9449) that a caller makes for each request when its tenant is under <c>fapi2</c>: a short signed JWT
/// that shows the caller holds the private key its access token is bound to. The check needs no store of seen
/// <c>jti</c> values and asks for no <c>nonce</c>, so a proof stays good for <see cref="IatWindow"/> either side of
/// the server's clock, for the same method, address and token only.
/// </summary>
internal static class DpopProof
{
    /// <summary>The signature algorithms a proof may use.</summary>
    public static readonly string[] Algorithms = ["ES256", "PS256"];

    /// <summary>How far a proof's <c>iat</c> may be from the server's clock, in either direction.</summary>
    public static readonly TimeSpan IatWindow = TimeSpan.FromSeconds(60);

    private const string ProofType = "dpop+jwt";

    /// <summary>
    /// Whether the request carries exactly one valid proof for the access token. The proof must be signed with the key in
    /// its own <c>jwk</c> header, which must hold no private key. Its <c>htm</c> must be the request's method, its
    /// <c>htu</c> the request's scheme, host and path (the query and fragment are ignored), its <c>iat</c> within
    /// <see cref="IatWindow"/> of <paramref name="now"/>, its <c>jti</c> not blank and its <c>ath</c> the hash of the
    /// access token. The key's thumbprint must equal <paramref name="expectedJkt"/>, the <c>cnf.jkt</c> of the token.
    /// </summary>
    /// <remarks>
    /// The scheme and host are the request's after the forwarded headers middleware, so a proof made for the address
    /// the caller used passes behind a listed proxy.
    /// </remarks>
    public static async Task<bool> IsValidAsync(HttpRequest request, string accessToken, string? expectedJkt, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(expectedJkt) || request.Headers["DPoP"] is not { Count: 1 } headers || string.IsNullOrWhiteSpace(headers[0]))
        {
            return false;
        }

        try
        {
            var handler = new JsonWebTokenHandler();
            var proof = handler.ReadJsonWebToken(headers[0]);
            if (proof.Typ != ProofType
                || !Algorithms.Contains(proof.Alg, StringComparer.Ordinal)
                || !proof.TryGetHeaderValue<JsonElement>("jwk", out var jwkElement)
                || jwkElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var jwk = new JsonWebKey(jwkElement.GetRawText());
            if (jwk.HasPrivateKey)
            {
                return false;
            }

            var result = await handler.ValidateTokenAsync(headers[0], new TokenValidationParameters
            {
                IssuerSigningKey = jwk,
                ValidAlgorithms = Algorithms,
                ValidTypes = [ProofType],
                RequireSignedTokens = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
            });
            if (!result.IsValid)
            {
                return false;
            }

            return proof.TryGetPayloadValue<string>("htm", out var method)
                && string.Equals(method, request.Method, StringComparison.Ordinal)
                && proof.TryGetPayloadValue<string>("htu", out var target)
                && TargetMatches(target, request)
                && proof.TryGetPayloadValue<string>("jti", out var id)
                && !string.IsNullOrWhiteSpace(id)
                && proof.TryGetPayloadValue<long>("iat", out var issuedAt)
                && (now - DateTimeOffset.FromUnixTimeSeconds(issuedAt)).Duration() <= IatWindow
                && proof.TryGetPayloadValue<string>("ath", out var hash)
                && string.Equals(hash, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken))), StringComparison.Ordinal)
                && string.Equals(Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint()), expectedJkt, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TargetMatches(string target, HttpRequest request) =>
        Uri.TryCreate(target, UriKind.Absolute, out var proofed)
        && Uri.TryCreate(UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path), UriKind.Absolute, out var actual)
        && Uri.Compare(proofed, actual, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal) == 0;
}
