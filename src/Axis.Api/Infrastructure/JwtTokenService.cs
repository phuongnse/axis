using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Api.Infrastructure;

internal sealed class JwtTokenService(IConfiguration configuration) : ITokenService
{
    private static readonly JwtSecurityTokenHandler Handler = new();

    public AccessTokenData GenerateAccessToken(
        Guid userId, Guid orgId, string email, string fullName,
        IReadOnlyList<string> permissions, Guid refreshTokenId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var ttl = int.Parse(configuration["Jwt:AccessTokenTtlMinutes"] ?? "15");
        var expires = now.AddMinutes(ttl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Email, email),
            new("name", fullName),
            new("org_id", orgId.ToString()),
            new("rt_id", refreshTokenId.ToString()),
        };

        foreach (var permission in permissions)
            claims.Add(new Claim("permissions", permission));

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return new AccessTokenData(Handler.WriteToken(token), jti, expires);
    }

    public (string RawToken, string TokenHash) GenerateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (raw, HashToken(raw));
    }

    public string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
