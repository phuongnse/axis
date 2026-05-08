using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;

namespace Axis.Api.Endpoints;

internal static class TokenHelper
{
    internal static async Task<IResult> IssueTokensAsync(
        Guid userId,
        Guid orgId,
        string email,
        string fullName,
        IReadOnlyList<string> permissions,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (rawRefresh, refreshHash) = tokenService.GenerateRefreshToken();
        var ttlDays = int.Parse(configuration["RefreshToken:TtlDays"] ?? "7");
        var refreshExpires = DateTime.UtcNow.AddDays(ttlDays);

        var refreshId = await refreshTokenStore.CreateAsync(
            userId, orgId, refreshHash,
            httpContext.Request.Headers.UserAgent.ToString(),
            refreshExpires, ct);

        var accessToken = tokenService.GenerateAccessToken(
            userId, orgId, email, fullName, permissions, refreshId);

        // Path scoped to /api/auth so the browser only sends the cookie on auth endpoints,
        // preventing accidental transmission to non-auth routes.
        httpContext.Response.Cookies.Append("refresh_token", rawRefresh, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = refreshExpires,
        });

        return Results.Ok(new
        {
            access_token = accessToken.Token,
            token_type = "Bearer",
            expires_in = (int)(accessToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
        });
    }
}
