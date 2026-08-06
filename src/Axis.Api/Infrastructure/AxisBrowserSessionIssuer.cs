using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Axis.Api.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Infrastructure;

internal static class BrowserSessionCorrelation
{
    public const string ClaimType = "axis:session_correlation";

    public static string Generate() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Digest(string correlation)
    {
        if (string.IsNullOrWhiteSpace(correlation))
            throw new InvalidOperationException("The browser session correlation is unavailable.");

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(correlation)))
            .ToLowerInvariant();
    }
}

internal sealed class AxisBrowserSessionIssuer(AxisBrowserSessionPolicy sessionPolicy)
{
    public Task RotateAsync(
        HttpContext httpContext,
        Guid userId,
        Guid? workspaceId,
        string email,
        string fullName,
        string? correlation = null) =>
        RotatePrincipalAsync(
            httpContext,
            CreatePrincipal(userId, workspaceId, email, fullName, correlation),
            sessionPolicy.CreateAuthenticationProperties());

    private static async Task RotatePrincipalAsync(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        AuthenticationProperties properties)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await httpContext.SignInAsync(
            AxisApiServiceExtensions.BrowserSessionRotationScheme,
            principal,
            properties);
    }

    private static ClaimsPrincipal CreatePrincipal(
        Guid userId,
        Guid? workspaceId,
        string email,
        string fullName,
        string? correlation)
    {
        List<Claim> claims =
        [
            new(Claims.Subject, userId.ToString()),
            new(Claims.Email, email),
            new(Claims.Name, fullName),
            new(BrowserSessionCorrelation.ClaimType, correlation ?? BrowserSessionCorrelation.Generate()),
        ];
        if (workspaceId is Guid resolvedWorkspaceId)
            claims.Add(new Claim("workspace_id", resolvedWorkspaceId.ToString()));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
