using System.Security.Claims;
using Axis.Identity.Application.Services;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Authorization;

internal sealed class ServiceTokenAuthorityValidationHandler(
    IServiceClientAssertionAuthentication authority)
    : IOpenIddictValidationHandler<OpenIddictValidationEvents.ProcessAuthenticationContext>
{
    public async ValueTask HandleAsync(OpenIddictValidationEvents.ProcessAuthenticationContext context)
    {
        ClaimsPrincipal? principal = context.AccessTokenPrincipal;
        if (principal is null || !string.Equals(principal.FindFirst("subject_kind")?.Value, "service", StringComparison.Ordinal))
            return;

        if (!Guid.TryParse(principal.GetClaim(Claims.Subject), out Guid serviceIdentityId)
            || !Guid.TryParse(principal.FindFirst("service_key_id")?.Value, out Guid keyId)
            || !await authority.HasActiveAuthorityAsync(serviceIdentityId, keyId, context.CancellationToken))
        {
            context.Reject(Errors.InvalidToken, "The service authority is no longer active.");
        }
    }
}
