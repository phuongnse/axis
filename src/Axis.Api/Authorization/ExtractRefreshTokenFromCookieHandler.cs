using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Axis.Api.Authorization;

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
