using Axis.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Axis.Server.Tests;

/// <summary>
/// The check of a DPoP proof that a <c>fapi2</c> tenant makes on each request. A valid proof passes, and a proof that
/// is wrong in one field fails. The check says only yes or no.
/// </summary>
public sealed class DpopProofTests : IDisposable
{
    private const string AccessToken = "header.payload.signature";
    private const string Url = "https://axis.example.test/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest";
    private static readonly DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly DpopTestKey _key = new();

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task A_valid_proof_passes()
    {
        var proof = Proof();

        Assert.True(await IsValidAsync(proof));
    }

    [Theory]
    [InlineData("https://axis.example.test/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest?a=1#top")]
    [InlineData("HTTPS://AXIS.EXAMPLE.TEST:443/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest")]
    public async Task The_query_the_fragment_the_letter_case_and_the_default_port_of_the_htu_do_not_matter(string htu)
    {
        Assert.True(await IsValidAsync(Proof(url: htu)));
    }

    [Theory]
    [InlineData("https://other.example.test/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest")]
    [InlineData("http://axis.example.test/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest")]
    [InlineData("https://axis.example.test:8443/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest")]
    [InlineData("https://axis.example.test/api/apps/Purchase_Requests/endpoints/Other")]
    [InlineData("/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest")]
    [InlineData("not a uri")]
    public async Task A_proof_for_another_address_fails(string htu)
    {
        Assert.False(await IsValidAsync(Proof(url: htu)));
    }

    [Fact]
    public async Task A_proof_for_another_method_fails()
    {
        Assert.False(await IsValidAsync(Proof(htm: "GET")));
    }

    [Theory]
    [InlineData(-61)]
    [InlineData(61)]
    public async Task A_proof_whose_iat_is_outside_the_window_fails(int seconds)
    {
        Assert.False(await IsValidAsync(Proof(issuedAt: _now.AddSeconds(seconds))));
    }

    [Theory]
    [InlineData(-59)]
    [InlineData(59)]
    public async Task A_proof_whose_iat_is_inside_the_window_passes(int seconds)
    {
        Assert.True(await IsValidAsync(Proof(issuedAt: _now.AddSeconds(seconds))));
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("at+jwt")]
    public async Task A_proof_with_another_typ_fails(string typ)
    {
        Assert.False(await IsValidAsync(Proof(typ: typ)));
    }

    [Fact]
    public async Task A_proof_with_alg_none_fails()
    {
        Assert.False(await IsValidAsync(Proof(alg: "none")));
    }

    [Fact]
    public async Task A_proof_whose_jwk_holds_a_private_key_fails()
    {
        Assert.False(await IsValidAsync(Proof(includePrivateKey: true)));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData(null)]
    public async Task A_proof_for_another_access_token_or_without_ath_fails(string? ath)
    {
        var proof = ath is null
            ? Proof(includeAth: false)
            : Proof(ath: DpopTestKey.Hash("another.access.token"));

        Assert.False(await IsValidAsync(proof));
    }

    [Fact]
    public async Task A_proof_without_a_jti_fails()
    {
        Assert.False(await IsValidAsync(Proof(jti: "")));
    }

    [Fact]
    public async Task Two_DPoP_headers_fail_even_when_both_proofs_are_valid()
    {
        Assert.False(await IsValidAsync(new StringValues([Proof(), Proof()])));
    }

    [Fact]
    public async Task A_missing_or_malformed_proof_fails()
    {
        Assert.False(await IsValidAsync(StringValues.Empty));
        Assert.False(await IsValidAsync("not-a-jwt"));
        Assert.False(await IsValidAsync(""));
    }

    [Fact]
    public async Task A_token_bound_to_another_key_or_to_no_key_fails()
    {
        using var other = new DpopTestKey();
        var proof = Proof();

        Assert.False(await IsValidAsync(proof, expectedJkt: other.Thumbprint));
        Assert.False(await IsValidAsync(proof, expectedJkt: null));
    }

    [Fact]
    public async Task A_proof_with_a_tampered_payload_fails()
    {
        var parts = Proof().Split('.');
        var other = Proof(url: "https://other.example.test/x").Split('.');

        Assert.False(await IsValidAsync($"{parts[0]}.{other[1]}.{parts[2]}"));
    }

    private string Proof(
        DateTimeOffset? issuedAt = null,
        string typ = "dpop+jwt",
        string alg = "ES256",
        string? jti = null,
        string? ath = null,
        bool includeAth = true,
        bool includePrivateKey = false,
        string? htm = null,
        string url = Url) =>
        _key.Proof("POST", url, AccessToken, issuedAt ?? _now, typ, alg, jti, ath, includeAth, includePrivateKey, htm);

    private Task<bool> IsValidAsync(StringValues proofs, string? expectedJkt = "") =>
        DpopProof.IsValidAsync(Request(proofs), AccessToken, expectedJkt == "" ? _key.Thumbprint : expectedJkt, _now);

    private static HttpRequest Request(StringValues proofs)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Post;
        request.Scheme = "https";
        request.Host = new HostString("axis.example.test");
        request.Path = "/api/apps/Purchase_Requests/endpoints/SubmitPurchaseRequest";
        if (proofs.Count > 0)
        {
            request.Headers["DPoP"] = proofs;
        }

        return request;
    }
}
