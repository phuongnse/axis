using System.Security.Claims;
using Axis.Api.Infrastructure;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Validation.OpenIddictValidationEvents;

namespace Axis.Api.Authorization;

internal sealed class ValidateJtiBlacklistHandler(IJtiBlacklist blacklist)
    : IOpenIddictValidationHandler<ProcessAuthenticationContext>
{
    public async ValueTask HandleAsync(ProcessAuthenticationContext context)
    {
        ClaimsPrincipal? principal = context.AccessTokenPrincipal;
        if (principal is null)
            return;

        string? tokenId = principal.FindFirstValue("jti")
            ?? principal.GetClaim(Claims.Private.TokenId);
        if (string.IsNullOrWhiteSpace(tokenId))
            return;

        if (await blacklist.IsBlacklistedAsync(tokenId, context.CancellationToken))
        {
            context.Reject(
                error: Errors.InvalidToken,
                description: "The access token has been revoked.",
                uri: null);
        }
    }
}
