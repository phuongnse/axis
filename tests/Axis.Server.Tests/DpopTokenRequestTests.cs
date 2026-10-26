using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Server.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Axis.Server.Tests;

/// <summary>
/// The token request of a <c>fapi2</c> sign-in, against a stub token endpoint that answers from a queue. Each test shows
/// what the request carries and what a refusal does.
/// </summary>
public sealed class DpopTokenRequestTests
{
    private const string TokenEndpoint = "https://idp.example.test/realms/axis/protocol/openid-connect/token";
    private static readonly DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_request_carries_one_proof_with_the_public_key_and_no_nonce()
    {
        var endpoint = new StubTokenEndpoint(Success());

        var response = await SendAsync(endpoint);

        Assert.Equal("id-token", response.IdToken);
        var call = Assert.Single(endpoint.Calls);
        var proof = new JsonWebToken(call.Dpop);
        Assert.Equal("dpop+jwt", proof.Typ);
        Assert.Equal("ES256", proof.Alg);
        Assert.Equal("POST", proof.GetPayloadValue<string>("htm"));
        Assert.Equal(TokenEndpoint, proof.GetPayloadValue<string>("htu"));
        Assert.Equal(_now.ToUnixTimeSeconds(), proof.GetPayloadValue<long>("iat"));
        Assert.False(string.IsNullOrWhiteSpace(proof.GetPayloadValue<string>("jti")));
        Assert.False(proof.TryGetPayloadValue<string>("nonce", out _));
        Assert.False(proof.TryGetPayloadValue<string>("ath", out _));
        var jwk = Jwk(proof);
        Assert.False(jwk.HasPrivateKey);
        Assert.Equal("EC", jwk.Kty);
        Assert.Contains("code=the-code", call.Form);
    }

    [Fact]
    public async Task A_proof_is_signed_with_its_own_key()
    {
        var endpoint = new StubTokenEndpoint(Success());

        await SendAsync(endpoint);

        var proof = endpoint.Calls[0].Dpop;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(proof, new TokenValidationParameters
        {
            IssuerSigningKey = Jwk(new JsonWebToken(proof)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task A_nonce_answer_gets_one_retry_with_the_same_key_the_nonce_and_a_new_request()
    {
        var endpoint = new StubTokenEndpoint(NonceRequired("n-1"), Success());
        var attempts = 0;

        var response = await SendAsync(endpoint, () => new OpenIdConnectMessage { Code = "the-code", ClientAssertion = $"assertion-{++attempts}" });

        Assert.Equal("id-token", response.IdToken);
        Assert.Equal(2, attempts);
        Assert.Equal(2, endpoint.Calls.Count);
        Assert.Contains("client_assertion=assertion-1", endpoint.Calls[0].Form);
        Assert.Contains("client_assertion=assertion-2", endpoint.Calls[1].Form);
        var first = new JsonWebToken(endpoint.Calls[0].Dpop);
        var second = new JsonWebToken(endpoint.Calls[1].Dpop);
        Assert.False(first.TryGetPayloadValue<string>("nonce", out _));
        Assert.Equal("n-1", second.GetPayloadValue<string>("nonce"));
        Assert.NotEqual(first.GetPayloadValue<string>("jti"), second.GetPayloadValue<string>("jti"));
        Assert.Equal(
            Base64UrlEncoder.Encode(Jwk(first).ComputeJwkThumbprint()),
            Base64UrlEncoder.Encode(Jwk(second).ComputeJwkThumbprint()));
    }

    [Fact]
    public async Task A_second_nonce_answer_fails_after_two_requests()
    {
        var endpoint = new StubTokenEndpoint(NonceRequired("n-1"), NonceRequired("n-2"));

        await Assert.ThrowsAsync<OpenIdConnectProtocolException>(() => SendAsync(endpoint));

        Assert.Equal(2, endpoint.Calls.Count);
    }

    [Fact]
    public async Task Another_refusal_fails_with_no_retry()
    {
        var endpoint = new StubTokenEndpoint(Answer(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Code not valid"}"""));

        var exception = await Assert.ThrowsAsync<OpenIdConnectProtocolException>(() => SendAsync(endpoint));

        Assert.Single(endpoint.Calls);
        Assert.Contains("invalid_grant", exception.Message);
        Assert.Contains("Code not valid", exception.Message);
    }

    [Fact]
    public async Task A_nonce_answer_with_no_nonce_header_fails_with_no_retry()
    {
        var endpoint = new StubTokenEndpoint(Answer(HttpStatusCode.BadRequest, """{"error":"use_dpop_nonce"}"""));

        await Assert.ThrowsAsync<OpenIdConnectProtocolException>(() => SendAsync(endpoint));

        Assert.Single(endpoint.Calls);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_fails()
    {
        var endpoint = new StubTokenEndpoint(Answer(HttpStatusCode.BadGateway, "<html>Bad gateway</html>"));

        await Assert.ThrowsAsync<OpenIdConnectProtocolException>(() => SendAsync(endpoint));

        Assert.Single(endpoint.Calls);
    }

    private static Task<OpenIdConnectMessage> SendAsync(StubTokenEndpoint endpoint, Func<OpenIdConnectMessage>? createRequest = null) =>
        DpopTokenRequest.SendAsync(
            new HttpClient(endpoint),
            TokenEndpoint,
            createRequest ?? (() => new OpenIdConnectMessage { Code = "the-code" }),
            new FixedClock(_now),
            CancellationToken.None);

    private static JsonWebKey Jwk(JsonWebToken proof)
    {
        Assert.True(proof.TryGetHeaderValue<JsonElement>("jwk", out var element));
        return new JsonWebKey(element.GetRawText());
    }

    private static HttpResponseMessage Success() => Answer(HttpStatusCode.OK, """{"id_token":"id-token","access_token":"bound","token_type":"DPoP"}""");

    private static HttpResponseMessage NonceRequired(string nonce)
    {
        var response = Answer(HttpStatusCode.BadRequest, """{"error":"use_dpop_nonce"}""");
        response.Headers.Add("DPoP-Nonce", nonce);
        return response;
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Call(string Dpop, string Form);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubTokenEndpoint(params HttpResponseMessage[] answers) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _answers = new(answers);

        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(TokenEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.True(request.Headers.TryGetValues("DPoP", out var proofs));
            Calls.Add(new Call(Assert.Single(proofs), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return _answers.Dequeue();
        }
    }
}
