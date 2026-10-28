using System.Numerics;
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

    /// <summary>The shortest RSA key a proof may be signed with, in bits (the floor of FAPI 2.0).</summary>
    public const int MinimumRsaKeyBits = 2048;

    private const string ProofType = "dpop+jwt";

    /// <summary>
    /// A proof that this server makes for a request it sends, signed with <paramref name="key"/>, which must be a P-256 key.
    /// Its <c>jwk</c> header holds the public key only. Its <c>htu</c> is the <paramref name="url"/> without the query and
    /// fragment, and it carries the <paramref name="nonce"/> when the receiver asked for one. It has no <c>ath</c>, as
    /// no access token exists yet at the token endpoint.
    /// </summary>
    public static string Create(ECDsa key, string method, string url, DateTimeOffset now, string? nonce = null)
    {
        var parameters = key.ExportParameters(false);
        var claims = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = method,
            ["htu"] = new Uri(url).GetLeftPart(UriPartial.Path),
            ["iat"] = now.ToUnixTimeSeconds(),
        };
        if (nonce is not null)
        {
            claims["nonce"] = nonce;
        }

        var jwk = new Dictionary<string, object>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
            ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = ProofType,
            Claims = claims,
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256)
            {
                // A cached signature provider would keep the ECDsa after the caller disposes it.
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
        });
    }

    /// <summary>
    /// Whether the request carries exactly one valid proof for the access token. The proof must be signed with the key in
    /// its own <c>jwk</c> header, which must hold no private key and, for an RSA key, a modulus of at least
    /// <see cref="MinimumRsaKeyBits"/> bits. Its <c>htm</c> must be the request's method, its
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
            if (jwk.HasPrivateKey || (jwk.Kty == "RSA" && !HasMinimumRsaKeySize(jwk)))
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

    // The library enforces only 1024 bits when it verifies, so the modulus is measured here.
    private static bool HasMinimumRsaKeySize(JsonWebKey jwk) =>
        !string.IsNullOrEmpty(jwk.N)
        && new BigInteger(Base64UrlEncoder.DecodeBytes(jwk.N), isUnsigned: true, isBigEndian: true).GetBitLength() >= MinimumRsaKeyBits;

    private static bool TargetMatches(string target, HttpRequest request) =>
        Uri.TryCreate(target, UriKind.Absolute, out var proofed)
        && Uri.TryCreate(UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path), UriKind.Absolute, out var actual)
        && Uri.Compare(proofed, actual, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal) == 0;
}
