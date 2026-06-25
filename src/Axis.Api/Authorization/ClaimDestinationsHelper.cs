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
            // Required subject/token identifiers stay in the access token.
            case Claims.Subject:
            case Claims.Private.TokenId:
                yield return Destinations.AccessToken;
                yield break;

            // OIDC claims enter the id_token only when the matching scope is granted.
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

            case "workspace_id":
                yield return Destinations.AccessToken;
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
