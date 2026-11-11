using System.Net;
using System.Text;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

/// <summary>
/// The token exchange of <see cref="RealmAgentTokens"/> posts the standard token exchange form as the agent client, with the scope and no
/// audience. A 400 is a refused grant, and every other failure is an error that names the step and the status, never the body.
/// </summary>
public sealed class RealmAgentTokensTests
{
    private const string Secret = "agent-secret-1";
    private const string Subject = "platform-access-token";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_200_with_both_tokens_gives_them_and_the_form_is_the_standard_exchange_of_the_agent_client()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "access_token": "agent-access", "refresh_token": "agent-refresh" }""");

        var exchanged = await Tokens(handler).ExchangeAsync(Subject, CancellationToken);

        Assert.False(exchanged.IsRefused);
        Assert.Equal("agent-access", exchanged.AccessToken);
        Assert.Equal("agent-refresh", exchanged.RefreshToken);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://idp.example.test/realms/acme/protocol/openid-connect/token", handler.Uri?.ToString());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = RealmAgentTokens.TokenExchangeGrant,
                ["client_id"] = RealmAgentClient.ClientId,
                ["client_secret"] = Secret,
                ["subject_token"] = Subject,
                ["subject_token_type"] = RealmAgentTokens.AccessTokenType,
                ["requested_token_type"] = RealmAgentTokens.RefreshTokenType,
                ["scope"] = RealmSync.AuthoringScope,
            },
            handler.Form);
    }

    [Fact]
    public async Task A_400_is_a_refused_grant_with_no_tokens()
    {
        var exchanged = await Tokens(new StubHandler(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""")).ExchangeAsync(Subject, CancellationToken);

        Assert.True(exchanged.IsRefused);
        Assert.Null(exchanged.AccessToken);
        Assert.Null(exchanged.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_other_status_throws_an_error_that_names_the_step_and_the_status_and_holds_no_body(HttpStatusCode status)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Tokens(new StubHandler(status, """{ "error": "invalid_client", "secret": "body-secret-9" }""")).ExchangeAsync(Subject, CancellationToken));

        Assert.Contains("exchange the token for the agent host", error.Message);
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.DoesNotContain("body-secret-9", error.Message);
        Assert.DoesNotContain(Secret, error.Message);
        Assert.DoesNotContain(Subject, error.Message);
    }

    [Theory]
    [InlineData("""{ "access_token": "agent-access" }""")]
    [InlineData("""{ "refresh_token": "agent-refresh" }""")]
    [InlineData("""{ "access_token": "", "refresh_token": "agent-refresh" }""")]
    public async Task A_200_without_both_tokens_throws(string body)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Tokens(new StubHandler(HttpStatusCode.OK, body)).ExchangeAsync(Subject, CancellationToken));

        Assert.DoesNotContain("agent-access", error.Message);
        Assert.DoesNotContain("agent-refresh", error.Message);
    }

    private static RealmAgentTokens Tokens(StubHandler handler) =>
        new(new HttpClient(handler), new TenantRealmAgent(new Uri("https://idp.example.test/"), "acme", new SecretValue(Secret)));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public Dictionary<string, string> Form { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Form = (await request.Content!.ReadAsStringAsync(cancellationToken))
                .Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
