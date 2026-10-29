using Axis.Tenancy.Realms;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Axis.Server.Authentication;

/// <summary>
/// The protocol validator of an ID token that a site client received: the token's <c>azp</c> is the site client, so it is the
/// client the validator expects in the <c>aud</c> claim. <c>OnTokenValidated</c> checks that this is the client that started
/// the sign-in.
/// </summary>
internal sealed class SiteClientProtocolValidator : OpenIdConnectProtocolValidator
{
    protected override void ValidateIdToken(OpenIdConnectProtocolValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (validationContext.ValidatedIdToken?.Payload.Azp is { } azp && azp.StartsWith(RealmSync.SiteClientPrefix, StringComparison.Ordinal))
        {
            validationContext.ClientId = azp;
        }

        base.ValidateIdToken(validationContext);
    }
}
