using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Authentication;

/// <summary>
/// The reason codes of a <c>session.signInRefused</c> audit record, and the rule that picks one
/// for a failed callback.
/// </summary>
internal static class SignInRefusals
{
    /// <summary>The callback's state came from another tenant's sign-in.</summary>
    public const string OtherTenant = "otherTenant";

    /// <summary>The ID token failed validation or has no user id.</summary>
    public const string InvalidToken = "invalidToken";

    /// <summary>The callback's state is missing, unreadable or fails the correlation check.</summary>
    public const string InvalidState = "invalidState";

    /// <summary>The identity provider answered with an error, or refused the code exchange.</summary>
    public const string ProviderError = "providerError";

    /// <summary>Picks the reason for a callback that failed. The checks run in this order.</summary>
    /// <param name="stateRead">Whether this tenant's sign-in client could read the callback's state.</param>
    /// <param name="otherTenantReadsState">Whether another tenant's sign-in client can read the state.</param>
    /// <param name="providerError">Whether the callback carries an <c>error</c> from the identity provider.</param>
    /// <param name="failure">The failure the OIDC handler reports.</param>
    /// <param name="tokenRejected">Whether the ID token was rejected for a missing user id.</param>
    public static string Classify(
        bool stateRead,
        bool otherTenantReadsState,
        bool providerError,
        Exception? failure,
        bool tokenRejected)
    {
        if (!stateRead)
        {
            return otherTenantReadsState ? OtherTenant : InvalidState;
        }

        if (providerError || failure is OpenIdConnectProtocolException)
        {
            return ProviderError;
        }

        if (tokenRejected || failure is SecurityTokenException || failure?.InnerException is SecurityTokenException)
        {
            return InvalidToken;
        }

        return InvalidState;
    }
}
