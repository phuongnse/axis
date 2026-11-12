using System.Net;
using System.Text;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

/// <summary>
/// The token exchange of <see cref="RealmAgentTokens"/> posts the standard token exchange form as the agent client, with the scope and no
/// audience. A 400 is a refused grant, and every other failure is an error that names the step and the status, never the body.
/// The refresh posts the refresh token form as the agent client. It keeps the given token when the realm rotates none, counts only a
/// 400 with <c>invalid_grant</c> as refused, and throws for any other failure.
/// </summary>
public sealed class RealmAgentTokensTests
{
    private const string Secret = "agent-secret-1";
    private const string Subject = "platform-access-token";
    private const string Refresh = "agent-refresh-1";

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

    [Fact]
    public async Task A_refresh_posts_the_refresh_token_form_of_the_agent_client_and_gives_the_rotated_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "access_token": "agent-access-2", "refresh_token": "agent-refresh-2" }""");

        var refreshed = await Tokens(handler).RefreshAsync(Refresh, CancellationToken);

        Assert.False(refreshed.IsRefused);
        Assert.Equal("agent-access-2", refreshed.AccessToken);
        Assert.Equal("agent-refresh-2", refreshed.RefreshToken);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://idp.example.test/realms/acme/protocol/openid-connect/token", handler.Uri?.ToString());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = RealmAgentClient.ClientId,
                ["client_secret"] = Secret,
                ["refresh_token"] = Refresh,
            },
            handler.Form);
    }

    [Fact]
    public async Task A_refresh_answer_without_a_refresh_token_keeps_the_given_one()
    {
        var refreshed = await Tokens(new StubHandler(HttpStatusCode.OK, """{ "access_token": "agent-access-2" }""")).RefreshAsync(Refresh, CancellationToken);

        Assert.Equal("agent-access-2", refreshed.AccessToken);
        Assert.Equal(Refresh, refreshed.RefreshToken);
    }

    [Fact]
    public async Task A_refresh_answered_with_400_and_invalid_grant_is_refused()
    {
        var refreshed = await Tokens(new StubHandler(HttpStatusCode.BadRequest, """{ "error": "invalid_grant", "error_description": "Token is not active" }"""))
            .RefreshAsync(Refresh, CancellationToken);

        Assert.True(refreshed.IsRefused);
        Assert.Null(refreshed.AccessToken);
        Assert.Null(refreshed.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "unauthorized_client", "secret": "body-secret-9" }""")]
    [InlineData(HttpStatusCode.BadRequest, "not json")]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "invalid_client", "secret": "body-secret-9" }""")]
    [InlineData(HttpStatusCode.InternalServerError, """{ "error": "invalid_grant", "secret": "body-secret-9" }""")]
    public async Task A_refresh_with_any_other_failure_throws_an_error_that_names_the_step_and_the_status_and_holds_no_body_and_no_secret(HttpStatusCode status, string body)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Tokens(new StubHandler(status, body)).RefreshAsync(Refresh, CancellationToken));

        Assert.Contains("refresh the agent host's token", error.Message);
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.DoesNotContain("body-secret-9", error.Message);
        Assert.DoesNotContain(Secret, error.Message);
        Assert.DoesNotContain(Refresh, error.Message);
    }

    [Theory]
    [InlineData("""{ "refresh_token": "agent-refresh-2" }""")]
    [InlineData("""{ "access_token": "" }""")]
    [InlineData("not json")]
    public async Task A_refresh_200_without_an_access_token_throws(string body)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Tokens(new StubHandler(HttpStatusCode.OK, body)).RefreshAsync(Refresh, CancellationToken));

        Assert.DoesNotContain("agent-refresh-2", error.Message);
        Assert.DoesNotContain(Refresh, error.Message);
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
