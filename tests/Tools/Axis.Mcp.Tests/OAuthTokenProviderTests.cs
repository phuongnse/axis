using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Axis.Mcp.Authentication;
using Axis.Mcp.Configuration;

namespace Axis.Mcp.Tests;

[Collection(nameof(OAuthLoopbackCollection))]
public sealed class OAuthTokenProviderTests
{
    [Fact]
    public async Task AuthorizationCallback_WhenSuccessful_UsesPublicPkceContractAndReturnsBearerToken()
    {
        const string authorizationCode = "authorization-code";
        const string accessTokenValue = "access-token";
        string redirectUri = $"http://127.0.0.1:{AxisMcpOptions.CallbackPort}/callback";
        RecordingTokenHandler handler = new(
            $"{{\"access_token\":\"{accessTokenValue}\",\"expires_in\":3600,\"token_type\":\"Bearer\"}}");
        OAuthTokenProvider provider = CreateProvider(TimeSpan.FromMinutes(1), handler, out CapturingBrowserLauncher browser);
        Task<string> accessToken = provider.GetAccessTokenAsync(CancellationToken.None);

        Uri authorizationUri = await browser.AuthorizationUri.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("code", GetQueryValue(authorizationUri, "response_type"));
        Assert.Equal(AxisMcpOptions.ClientId, GetQueryValue(authorizationUri, "client_id"));
        Assert.Equal(redirectUri, GetQueryValue(authorizationUri, "redirect_uri"));
        Assert.Equal("S256", GetQueryValue(authorizationUri, "code_challenge_method"));
        Assert.Equal(
            ["client_id", "code_challenge", "code_challenge_method", "redirect_uri", "response_type", "scope", "state"],
            authorizationUri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]))
                .Order(StringComparer.Ordinal));

        await SendCallbackAsync(
            AxisMcpOptions.CallbackPort,
            $"/callback?code={authorizationCode}&state={GetQueryValue(authorizationUri, "state")}",
            TestContext.Current.CancellationToken);

        Assert.Equal(accessTokenValue, await accessToken);
        Assert.Equal(1, handler.TokenPostCount);
        Assert.Equal(HttpMethod.Post, handler.TokenRequestMethod);
        Assert.Equal("https://localhost:5281/connect/token", handler.TokenRequestUri?.ToString());

        string tokenRequestBody = Assert.IsType<string>(handler.TokenRequestBody);
        Uri tokenForm = new($"https://localhost/?{tokenRequestBody}");
        string codeVerifier = GetQueryValue(tokenForm, "code_verifier");
        string expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expectedChallenge, GetQueryValue(authorizationUri, "code_challenge"));
        Assert.Equal("authorization_code", GetQueryValue(tokenForm, "grant_type"));
        Assert.Equal(AxisMcpOptions.ClientId, GetQueryValue(tokenForm, "client_id"));
        Assert.Equal(redirectUri, GetQueryValue(tokenForm, "redirect_uri"));
        Assert.Equal(authorizationCode, GetQueryValue(tokenForm, "code"));
        Assert.Equal(
            ["client_id", "code", "code_verifier", "grant_type", "redirect_uri"],
            tokenRequestBody.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AuthorizationTimeout_WhenDeadlineExpires_ReleasesCallbackPort()
    {
        RecordingTokenHandler handler = new();
        OAuthTokenProvider provider = CreateProvider(TimeSpan.Zero, handler, out _);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal("Axis MCP authorization timed out.", exception.Message);
        Assert.Equal(0, handler.TokenPostCount);

        using CancellationTokenSource cancellation = new();
        Task<string> freshCallback = new LoopbackAuthorizationListener(AxisMcpOptions.CallbackPort)
            .WaitForCodeAsync("fresh-state", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => freshCallback);
    }

    [Fact]
    public async Task AuthorizationCallback_WhenStateDiffers_DoesNotExchangeAToken()
    {
        RecordingTokenHandler handler = new();
        OAuthTokenProvider provider = CreateProvider(TimeSpan.FromMinutes(1), handler, out CapturingBrowserLauncher browser);
        Task<string> accessToken = provider.GetAccessTokenAsync(CancellationToken.None);

        Uri authorizationUri = await browser.AuthorizationUri.WaitAsync(TestContext.Current.CancellationToken);
        await SendCallbackAsync(
            AxisMcpOptions.CallbackPort,
            $"/callback?code=authorization-code&state=wrong-{GetQueryValue(authorizationUri, "state")}",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accessToken);
        Assert.Equal(0, handler.TokenPostCount);
    }

    [Fact]
    public async Task AuthorizationCallback_WhenCodeIsMissing_DoesNotExchangeAToken()
    {
        RecordingTokenHandler handler = new();
        OAuthTokenProvider provider = CreateProvider(TimeSpan.FromMinutes(1), handler, out CapturingBrowserLauncher browser);
        Task<string> accessToken = provider.GetAccessTokenAsync(CancellationToken.None);

        Uri authorizationUri = await browser.AuthorizationUri.WaitAsync(TestContext.Current.CancellationToken);
        await SendCallbackAsync(
            AxisMcpOptions.CallbackPort,
            $"/callback?state={GetQueryValue(authorizationUri, "state")}",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accessToken);
        Assert.Equal(0, handler.TokenPostCount);
    }

    [Fact]
    public async Task Authorization_WhenCallerCancels_PreservesOperationCanceledException()
    {
        RecordingTokenHandler handler = new();
        OAuthTokenProvider provider = CreateProvider(TimeSpan.FromMinutes(1), handler, out CapturingBrowserLauncher browser);
        using CancellationTokenSource cancellation = new();
        Task<string> accessToken = provider.GetAccessTokenAsync(cancellation.Token);

        await browser.AuthorizationUri.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accessToken);
        Assert.Equal(0, handler.TokenPostCount);
    }

    private static OAuthTokenProvider CreateProvider(
        TimeSpan authorizationTimeout,
        RecordingTokenHandler handler,
        out CapturingBrowserLauncher browser)
    {
        browser = new CapturingBrowserLauncher();
        AxisMcpOptions options = AxisMcpOptions.Create(
            new Uri("https://localhost:5281/"),
            "test-root.pem") with
        {
            AuthorizationTimeout = authorizationTimeout,
        };

        return new OAuthTokenProvider(new HttpClient(handler), options, browser);
    }

    private static string GetQueryValue(Uri uri, string key)
    {
        string? value = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], key, StringComparison.Ordinal))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .SingleOrDefault();

        Assert.NotNull(value);
        return value;
    }

    private static async Task SendCallbackAsync(
        int port,
        string target,
        CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        await ConnectWithRetryAsync(client, port, cancellationToken);
        await using NetworkStream stream = client.GetStream();
        byte[] request = Encoding.ASCII.GetBytes(
            $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);

        byte[] response = new byte[4 * 1024];
        while (await stream.ReadAsync(response, cancellationToken) > 0)
        {
        }
    }

    private static async Task ConnectWithRetryAsync(
        TcpClient client,
        int port,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException) when (attempt < 49)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }

        throw new InvalidOperationException("The test callback listener did not start.");
    }

    private sealed class CapturingBrowserLauncher : IBrowserLauncher
    {
        private readonly TaskCompletionSource<Uri> _authorizationUri = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Uri> AuthorizationUri => _authorizationUri.Task;

        public bool TryOpen(Uri uri)
        {
            _authorizationUri.TrySetResult(uri);
            return true;
        }
    }

    private sealed class RecordingTokenHandler(string responseBody = "") : HttpMessageHandler
    {
        public int TokenPostCount { get; private set; }
        public HttpMethod? TokenRequestMethod { get; private set; }
        public Uri? TokenRequestUri { get; private set; }
        public string? TokenRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/connect/token")
            {
                TokenPostCount++;
                TokenRequestMethod = request.Method;
                TokenRequestUri = request.RequestUri;
                TokenRequestBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
