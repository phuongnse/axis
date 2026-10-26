using System.Security.Claims;
using System.Security.Cryptography;
using Axis.Tenancy;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Authentication;

/// <summary>
/// The signed JWT that a <c>fapi2</c> tenant's client sends to the identity provider in place of a client secret
/// (<c>private_key_jwt</c>, RFC 7523). Client keys are EC P-256 keys, and the assertion is signed with ES256.
/// </summary>
internal static class ClientAssertion
{
    public const string Type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static readonly TimeSpan _lifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Creates an assertion for one request to the identity provider. The identity provider refuses a reused
    /// <c>jti</c>, so each request needs its own assertion.
    /// </summary>
    /// <param name="authority">The tenant's authority, which is the audience.</param>
    /// <param name="key">The client key to sign with. Its key id goes in the <c>kid</c> header.</param>
    public static string Create(string clientId, string authority, TenantClientKey key, DateTimeOffset now)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(key.PrivateKey.Reveal());
        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = key.KeyId };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256)
        {
            // A cached signature provider would keep the ECDsa after it is disposed here.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = clientId,
            Audience = authority,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, clientId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime + _lifetime,
            SigningCredentials = credentials,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
