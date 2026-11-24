using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Axis.Tenancy.Keys;

/// <summary>
/// The RFC 7638 JWK thumbprint of an EC P-256 public key, which is the key id of the client keys and of the agent host key. It is the
/// SHA-256 hash of the key's required members in order, <c>{"crv":"P-256","kty":"EC","x":..,"y":..}</c>, as base64url. See
/// <c>docs/reference/keys-and-data-protection.md#key-policy</c>.
/// </summary>
public static class JwkThumbprint
{
    /// <summary>The key id and the base64url coordinates of <paramref name="publicKey"/>, which must hold <c>Q.X</c> and <c>Q.Y</c>.</summary>
    public static (string KeyId, string X, string Y) Of(ECParameters publicKey)
    {
        var x = Base64Url.EncodeToString(publicKey.Q.X!);
        var y = Base64Url.EncodeToString(publicKey.Q.Y!);
        var input = $$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""";
        return (Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(input))), x, y);
    }
}
