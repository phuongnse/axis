using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Tests;

/// <summary>The reason code of a refused sign-in callback, for each way the callback can fail.</summary>
public sealed class SignInRefusalsTests
{
    [Fact]
    public void State_that_only_another_tenant_can_read_is_the_other_tenant()
    {
        var reason = SignInRefusals.Classify(stateRead: false, otherTenantReadsState: true, providerError: false, failure: null, tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.OtherTenant, reason);
    }

    [Fact]
    public void State_that_nobody_can_read_is_an_invalid_state()
    {
        var reason = SignInRefusals.Classify(stateRead: false, otherTenantReadsState: false, providerError: false, failure: null, tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.InvalidState, reason);
    }

    [Fact]
    public void State_that_is_unreadable_wins_over_every_other_signal()
    {
        var reason = SignInRefusals.Classify(
            stateRead: false,
            otherTenantReadsState: false,
            providerError: true,
            new SecurityTokenInvalidSignatureException(),
            tokenRejected: true,
            issuerRejected: true);

        Assert.Equal(SignInRefusals.InvalidState, reason);
    }

    [Fact]
    public void A_rejected_issuer_is_an_invalid_issuer()
    {
        var reason = SignInRefusals.Classify(
            stateRead: true,
            otherTenantReadsState: false,
            providerError: false,
            new AuthenticationFailureException("The authorization response's iss is not the tenant's authority."),
            tokenRejected: false,
            issuerRejected: true);

        Assert.Equal(SignInRefusals.InvalidIssuer, reason);
    }

    [Fact]
    public void An_unreadable_state_wins_over_a_rejected_issuer()
    {
        var reason = SignInRefusals.Classify(stateRead: false, otherTenantReadsState: false, providerError: false, failure: null, tokenRejected: false, issuerRejected: true);

        Assert.Equal(SignInRefusals.InvalidState, reason);
    }

    [Fact]
    public void An_error_from_the_identity_provider_is_a_provider_error()
    {
        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: true, failure: null, tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.ProviderError, reason);
    }

    [Fact]
    public void A_refused_code_exchange_is_a_provider_error()
    {
        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: false, new OpenIdConnectProtocolException(), tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.ProviderError, reason);
    }

    [Fact]
    public void A_nonce_that_does_not_match_is_an_invalid_token()
    {
        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: false, new OpenIdConnectProtocolInvalidNonceException(), tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.InvalidToken, reason);
    }

    [Fact]
    public void A_token_that_fails_validation_is_an_invalid_token()
    {
        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: false, new SecurityTokenInvalidSignatureException(), tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.InvalidToken, reason);
    }

    [Fact]
    public void A_token_failure_wrapped_in_another_exception_is_an_invalid_token()
    {
        var failure = new InvalidOperationException("wrapped", new SecurityTokenExpiredException());

        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: false, failure, tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.InvalidToken, reason);
    }

    [Fact]
    public void A_token_without_a_user_id_is_an_invalid_token()
    {
        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: false, new AuthenticationFailureException("The ID token has no user id claim."), tokenRejected: true, issuerRejected: false);

        Assert.Equal(SignInRefusals.InvalidToken, reason);
    }

    [Fact]
    public void Any_other_failure_is_an_invalid_state()
    {
        var reason = SignInRefusals.Classify(stateRead: true, otherTenantReadsState: false, providerError: false, new AuthenticationFailureException("Correlation failed."), tokenRejected: false, issuerRejected: false);

        Assert.Equal(SignInRefusals.InvalidState, reason);
    }

    [Fact]
    public void A_refused_provider_is_a_provider_not_accepted()
    {
        var reason = SignInRefusals.Classify(
            stateRead: true,
            otherTenantReadsState: false,
            providerError: false,
            new AuthenticationFailureException("The platform sign-in does not accept the provider."),
            tokenRejected: false,
            issuerRejected: false,
            providerRefused: true);

        Assert.Equal(SignInRefusals.ProviderNotAccepted, reason);
    }

    [Fact]
    public void An_unreadable_state_wins_over_a_refused_provider()
    {
        var reason = SignInRefusals.Classify(stateRead: false, otherTenantReadsState: false, providerError: false, failure: null, tokenRejected: false, issuerRejected: false, providerRefused: true);

        Assert.Equal(SignInRefusals.InvalidState, reason);
    }
}
