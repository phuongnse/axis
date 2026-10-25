using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Tests;

/// <summary>
/// A caller's DPoP key and the proofs it makes. Each parameter of <see cref="Proof"/> that has a default is a field that
/// a test may break, so one proof differs from a valid one in one thing.
/// </summary>
internal sealed class DpopTestKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The RFC 7638 thumbprint of the public key, which is the <c>jkt</c> of a token bound to it.</summary>
    public string Thumbprint
    {
        get
        {
            var parameters = _key.ExportParameters(false);
            var members = $$"""{"crv":"P-256","kty":"EC","x":"{{Base64UrlEncoder.Encode(parameters.Q.X)}}","y":"{{Base64UrlEncoder.Encode(parameters.Q.Y)}}"}""";
            return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(members)));
        }
    }

    /// <summary>The hash of an access token that a proof carries as <c>ath</c>.</summary>
    public static string Hash(string accessToken) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));

    /// <summary>
    /// A proof for the method and address, made for the access token. A null <paramref name="issuedAt"/> is now, a null
    /// <paramref name="jti"/> is a new id, a null <paramref name="ath"/> is the token's hash and <paramref name="alg"/>
    /// <c>none</c> gives an unsigned proof. The <paramref name="issuedAt"/> is in seconds and a test that moves the
    /// clock passes the time it wants.
    /// </summary>
    public string Proof(
        string method,
        string url,
        string accessToken,
        DateTimeOffset? issuedAt = null,
        string typ = "dpop+jwt",
        string alg = "ES256",
        string? jti = null,
        string? ath = null,
        bool includeAth = true,
        bool includePrivateKey = false,
        string? htm = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["htm"] = htm ?? method,
            ["htu"] = url,
            ["iat"] = (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
        };
        if (jti != "")
        {
            claims["jti"] = jti ?? Guid.NewGuid().ToString("N");
        }

        if (includeAth)
        {
            claims["ath"] = ath ?? Hash(accessToken);
        }

        var jwk = PublicJwk();
        if (includePrivateKey)
        {
            jwk["d"] = Base64UrlEncoder.Encode(_key.ExportParameters(true).D);
        }

        if (alg == "none")
        {
            var header = JsonSerializer.Serialize(new Dictionary<string, object> { ["typ"] = typ, ["alg"] = "none", ["jwk"] = jwk });
            return $"{Base64UrlEncoder.Encode(header)}.{Base64UrlEncoder.Encode(JsonSerializer.Serialize(claims))}.";
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            TokenType = typ,
            Claims = claims,
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_key), alg),
        });
    }

    public void Dispose() => _key.Dispose();

    private Dictionary<string, object> PublicJwk()
    {
        var parameters = _key.ExportParameters(false);
        return new Dictionary<string, object>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
            ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
        };
    }
}
