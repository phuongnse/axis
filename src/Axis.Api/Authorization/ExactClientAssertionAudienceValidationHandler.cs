using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Authorization;

/// <summary>
/// Keeps Axis' approved service-authentication contract stricter than the
/// OpenIddict 7.6 default by requiring the exact configured token endpoint as
/// the single client-assertion audience.
/// </summary>
internal sealed class ExactClientAssertionAudienceValidationHandler(
    IConfiguration configuration)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessAuthenticationContext>
{
    public ValueTask HandleAsync(OpenIddictServerEvents.ProcessAuthenticationContext context)
    {
        ClaimsPrincipal? principal = context.ClientAssertionPrincipal;
        if (principal is null)
            return ValueTask.CompletedTask;

        string? configuredIssuer = configuration["OpenIddict:Issuer"];
        if (!Uri.TryCreate(configuredIssuer, UriKind.Absolute, out Uri? issuer))
            throw new InvalidOperationException("OpenIddict:Issuer must be an absolute URI.");

        Uri tokenEndpoint = new(issuer, "/connect/token");
        string? audience = principal.GetClaim(Claims.Audience);
        if (!Uri.TryCreate(audience, UriKind.Absolute, out Uri? value)
            || !Uri.Equals(value, tokenEndpoint))
        {
            context.Reject(
                Errors.InvalidGrant,
                "The client assertion audience is invalid.");
        }

        return ValueTask.CompletedTask;
    }
}
