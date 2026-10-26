using System.Security.Cryptography;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Tests;

public sealed class ClientAssertionTests
{
    private const string ClientId = "axis-fapi";
    private const string Authority = "http://localhost:8080/realms/axis";

    private static readonly DateTimeOffset _now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_assertion_names_the_client_and_the_authority_and_is_signed_with_the_key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new TenantClientKey("key-2", new SecretValue(ecdsa.ExportPkcs8PrivateKeyPem()));

        var assertion = ClientAssertion.Create(ClientId, Authority, key, _now);

        var token = new JsonWebToken(assertion);
        Assert.Equal("ES256", token.Alg);
        Assert.Equal("key-2", token.Kid);
        Assert.Equal(ClientId, token.Issuer);
        Assert.Equal(ClientId, token.Subject);
        Assert.Equal([Authority], token.Audiences);
        Assert.True(Guid.TryParse(token.Id, out _));
        Assert.Equal(_now.UtcDateTime, token.IssuedAt);
        Assert.Equal(_now.UtcDateTime.AddSeconds(60), token.ValidTo);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            assertion,
            new TokenValidationParameters
            {
                ValidIssuer = ClientId,
                ValidAudience = Authority,
                IssuerSigningKey = new ECDsaSecurityKey(ECDsa.Create(ecdsa.ExportParameters(includePrivateParameters: false))),
                ValidateLifetime = false,
            });
        Assert.True(result.IsValid, result.Exception?.ToString());
    }

    [Fact]
    public async Task An_assertion_does_not_verify_with_another_key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new TenantClientKey("key-1", new SecretValue(ecdsa.ExportPkcs8PrivateKeyPem()));

        var assertion = ClientAssertion.Create(ClientId, Authority, key, _now);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            assertion,
            new TokenValidationParameters
            {
                ValidIssuer = ClientId,
                ValidAudience = Authority,
                IssuerSigningKey = new ECDsaSecurityKey(ECDsa.Create(other.ExportParameters(includePrivateParameters: false))),
                ValidateLifetime = false,
            });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Each_assertion_has_a_fresh_jti()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new TenantClientKey("key-1", new SecretValue(ecdsa.ExportPkcs8PrivateKeyPem()));

        var first = new JsonWebToken(ClientAssertion.Create(ClientId, Authority, key, _now));
        var second = new JsonWebToken(ClientAssertion.Create(ClientId, Authority, key, _now));

        Assert.NotEqual(first.Id, second.Id);
    }
}
