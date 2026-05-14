using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Axis.Api.Authorization;

/// <summary>
/// Moves the refresh token out of the JSON response body into a httpOnly cookie.
///
/// WHY: Storing the refresh token in an httpOnly cookie prevents JavaScript from
/// accessing it (XSS protection). The access token lives in memory only.
/// </summary>
public sealed class ApplyRefreshTokenCookieHandler
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

        httpRequest.HttpContext.Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            // Secure only over HTTPS — allows the httpOnly cookie to be sent in test
            // environments that use plain HTTP (e.g. WebApplicationFactory).
            Secure = httpRequest.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/connect",
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });

        // Remove from the response JSON — the refresh token lives in the cookie only
        context.Response.RefreshToken = null;

        return default;
    }
}

/// <summary>
/// Reads the refresh token from the httpOnly cookie on refresh requests and
/// injects it into the token request so OpenIddict can validate it normally.
/// </summary>
public sealed class ExtractRefreshTokenFromCookieHandler
    : IOpenIddictServerHandler<ExtractTokenRequestContext>
{
    public ValueTask HandleAsync(ExtractTokenRequestContext context)
    {
        if (!string.Equals(
                context.Request?.GrantType,
                OpenIddictConstants.GrantTypes.RefreshToken,
                StringComparison.OrdinalIgnoreCase))
            return default;

        // Only inject from cookie if the body doesn't already contain a token
        if (!string.IsNullOrEmpty(context.Request?.RefreshToken))
            return default;

        HttpRequest? httpRequest = context.Transaction.GetHttpRequest();
        string? cookieToken = httpRequest?.Cookies["refresh_token"];

        if (!string.IsNullOrEmpty(cookieToken) && context.Request is not null)
            context.Request.RefreshToken = cookieToken;

        return default;
    }
}
