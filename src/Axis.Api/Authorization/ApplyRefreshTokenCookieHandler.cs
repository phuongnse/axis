using Microsoft.AspNetCore;
using Microsoft.Extensions.Configuration;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Axis.Api.Authorization;

/// <summary>
/// Moves the refresh token out of the JSON response body into a httpOnly cookie.
///
/// WHY: Storing the refresh token in an httpOnly cookie prevents JavaScript from
/// accessing it (XSS protection). The access token lives in memory only.
/// </summary>
public sealed class ApplyRefreshTokenCookieHandler(IConfiguration configuration)
    : IOpenIddictServerHandler<ApplyTokenResponseContext>
{
    public ValueTask HandleAsync(ApplyTokenResponseContext context)
    {
        string? refreshToken = context.Response.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
            return default;

        // GetHttpRequest returns the Microsoft.AspNetCore.Http.HttpRequest associated
        // with this OpenIddict server transaction; HttpContext is accessible from it.
        HttpRequest? httpRequest = context.Transaction.GetHttpRequest();
        if (httpRequest is null)
            return default;

        int refreshTokenTtlDays = configuration.GetValue("RefreshToken:TtlDays", 7);

        httpRequest.HttpContext.Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            // Secure only over HTTPS — allows the httpOnly cookie to be sent in test
            // environments that use plain HTTP (e.g. WebApplicationFactory).
            Secure = httpRequest.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/connect",
            Expires = DateTimeOffset.UtcNow.AddDays(refreshTokenTtlDays),
        });

        // Remove from the response JSON — the refresh token lives in the cookie only
        context.Response.RefreshToken = null;

        return default;
    }
}
