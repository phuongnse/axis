namespace Axis.Api.Infrastructure;

public record AccessTokenData(string Token, string Jti, DateTime ExpiresAt);

public interface ITokenService
{
    AccessTokenData GenerateAccessToken(
        Guid userId, Guid orgId, string email, string fullName,
        IReadOnlyList<string> permissions, Guid refreshTokenId);

    (string RawToken, string TokenHash) GenerateRefreshToken();

    string HashToken(string rawToken);
}
