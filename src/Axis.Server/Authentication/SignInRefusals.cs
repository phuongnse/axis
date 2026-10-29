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

    /// <summary>The ID token failed validation, has no user id, or has an <c>auth_time</c> that is missing or too old, carries a nonce that the sign-in did not send, or is not for the client that started the sign-in.</summary>
    public const string InvalidToken = "invalidToken";

    /// <summary>The callback's state is missing, unreadable or fails the correlation check.</summary>
    public const string InvalidState = "invalidState";

    /// <summary>
    /// The provider metadata's issuer is not the tenant's authority, or under <c>fapi2</c> the authorization response's
    /// <c>iss</c> is missing or is not the tenant's authority.
    /// </summary>
    public const string InvalidIssuer = "invalidIssuer";

    /// <summary>The identity provider answered with an error, or refused the code exchange.</summary>
    public const string ProviderError = "providerError";

    /// <summary>
    /// The ID token is valid, but the tenant's platform sign-in does not accept the provider that signed the person in,
    /// or does not accept accounts.
    /// </summary>
    public const string ProviderNotAccepted = "providerNotAccepted";

    /// <summary>
    /// The ID token is valid, but the site that started the sign-in does not allow the provider that signed the person in,
    /// or is no longer active.
    /// </summary>
    public const string ProviderNotAllowed = "providerNotAllowed";

    /// <summary>Picks the reason for a callback that failed. The checks run in this order.</summary>
    /// <param name="stateRead">Whether this tenant's sign-in client could read the callback's state.</param>
    /// <param name="otherTenantReadsState">Whether another tenant's sign-in client can read the state.</param>
    /// <param name="providerError">Whether the callback carries an <c>error</c> from the identity provider.</param>
    /// <param name="failure">The failure the OIDC handler reports.</param>
    /// <param name="tokenRejected">Whether the ID token was rejected for a missing user id or a missing or stale <c>auth_time</c>.</param>
    /// <param name="issuerRejected">Whether the metadata's issuer, or under <c>fapi2</c> the callback's <c>iss</c>, was rejected.</param>
    /// <param name="providerRefused">Whether the platform sign-in refused the provider, or the account sign-in.</param>
    /// <param name="providerNotAllowed">Whether the site that started the sign-in refused the provider, or is no longer active.</param>
    public static string Classify(
        bool stateRead,
        bool otherTenantReadsState,
        bool providerError,
        Exception? failure,
        bool tokenRejected,
        bool issuerRejected,
        bool providerRefused = false,
        bool providerNotAllowed = false)
    {
        if (!stateRead)
        {
            return otherTenantReadsState ? OtherTenant : InvalidState;
        }

        if (issuerRejected)
        {
            return InvalidIssuer;
        }

        if (providerNotAllowed)
        {
            return ProviderNotAllowed;
        }

        if (providerRefused)
        {
            return ProviderNotAccepted;
        }

        // A nonce mismatch is a refused token, not a failure of the provider.
        if (providerError || failure is OpenIdConnectProtocolException and not OpenIdConnectProtocolInvalidNonceException)
        {
            return ProviderError;
        }

        if (tokenRejected || failure is OpenIdConnectProtocolInvalidNonceException || failure is SecurityTokenException || failure?.InnerException is SecurityTokenException)
        {
            return InvalidToken;
        }

        return InvalidState;
    }
}
