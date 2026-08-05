using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Axis.Mcp.Configuration;

namespace Axis.Mcp.Authentication;

public sealed class OAuthTokenProvider(
    HttpClient httpClient,
    AxisMcpOptions options,
    IBrowserLauncher browserLauncher) : IAxisAccessTokenProvider
{
    private readonly SemaphoreSlim _authorizationLock = new(1, 1);
    private OAuthAccessToken? _token;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        OAuthAccessToken? current = Volatile.Read(ref _token);
        if (current is not null && !current.IsExpired)
            return current.AccessToken;

        await _authorizationLock.WaitAsync(cancellationToken);
        try
        {
            current = Volatile.Read(ref _token);
            if (current is not null && !current.IsExpired)
                return current.AccessToken;

            OAuthAccessToken refreshed = await AuthorizeAsync(cancellationToken);
            Volatile.Write(ref _token, refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _authorizationLock.Release();
        }
    }

    public void Invalidate() => Volatile.Write(ref _token, null);

    private async Task<OAuthAccessToken> AuthorizeAsync(CancellationToken cancellationToken)
    {
        string state = CreateRandomValue();
        string codeVerifier = CreateRandomValue();
        string codeChallenge = CreateCodeChallenge(codeVerifier);

        Uri authorizationUri = BuildAuthorizationUri(state, codeChallenge);
        using CancellationTokenSource authorizationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authorizationCancellation.CancelAfter(options.AuthorizationTimeout);
        LoopbackAuthorizationListener listener = new(AxisMcpOptions.CallbackPort);
        Task<string> callbackTask = listener.WaitForCodeAsync(state, authorizationCancellation.Token);

        if (!browserLauncher.TryOpen(authorizationUri))
        {
            await Console.Error.WriteLineAsync(
                "Axis MCP could not open a browser. Open this URL to authorize the local MCP client:");
            await Console.Error.WriteLineAsync(authorizationUri.ToString());
        }

        string authorizationCode;
        try
        {
            authorizationCode = await callbackTask;
        }
        catch (OperationCanceledException ex) when (
            authorizationCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Axis MCP authorization timed out.", ex);
        }

        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = AxisMcpOptions.ClientId,
            ["redirect_uri"] = options.RedirectUri.ToString(),
            ["code_verifier"] = codeVerifier,
            ["code"] = authorizationCode,
        });

        using HttpResponseMessage response = await httpClient.PostAsync(
            options.TokenEndpoint,
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Axis MCP token exchange failed with HTTP {(int)response.StatusCode} {response.StatusCode}.");

        OAuthTokenResponse? tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
        if (tokenResponse is null ||
            string.IsNullOrWhiteSpace(tokenResponse.AccessToken) ||
            tokenResponse.ExpiresIn <= 0)
            throw new InvalidOperationException("Axis MCP token exchange returned an invalid response.");

        return new OAuthAccessToken(
            tokenResponse.AccessToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, tokenResponse.ExpiresIn - 60)));
    }

    private Uri BuildAuthorizationUri(string state, string codeChallenge)
    {
        Dictionary<string, string> query = new()
        {
            ["response_type"] = "code",
            ["client_id"] = AxisMcpOptions.ClientId,
            ["redirect_uri"] = options.RedirectUri.ToString(),
            ["scope"] = "openid email profile",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };

        string queryString = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        UriBuilder builder = new(options.AuthorizationEndpoint)
        {
            Query = queryString,
        };
        return builder.Uri;
    }

    private static string CreateRandomValue()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string CreateCodeChallenge(string verifier)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record OAuthAccessToken(string AccessToken, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType);
}
