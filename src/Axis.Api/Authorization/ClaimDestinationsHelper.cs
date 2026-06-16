using System.Security.Claims;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Authorization;

/// <summary>
/// Sets claim destinations so OpenIddict knows which claims to include in
/// the access token, the id_token, or both.
/// </summary>
public static class ClaimDestinationsHelper
{
    public static void SetDestinations(ClaimsPrincipal principal)
    {
        foreach (Claim claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim, principal));
        }
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        switch (claim.Type)
        {
            // sub and jti are always in the access token
            case Claims.Subject:
            case Claims.Private.TokenId:
                yield return Destinations.AccessToken;
                yield break;

            // Standard OIDC claims go in the id_token when openid scope is granted,
            // and also in the access token so resource servers can read them
            case Claims.Email:
            case "email_verified":
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;
                yield break;

            case "name":
            case Claims.GivenName:
            case Claims.FamilyName:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;
                yield break;

            // Custom claims for Axis — always access token only
            case "workspace_id":
            case "permissions":
                yield return Destinations.AccessToken;
                yield break;

            // Everything else: access token only
            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
