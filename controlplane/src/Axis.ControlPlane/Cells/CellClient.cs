using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Axis.ControlPlane.Cells;

/// <summary>
/// The control plane's client of one cell's provisioning API, see <c>docs/reference/control-plane.md#reaching-the-cells</c>.
/// Every call goes over the mutual-TLS <see cref="HttpClient"/> it is given, to an <c>https</c> address only, with a bearer
/// token of the cell's operator realm. The token comes from the client credentials grant and stays until
/// <see cref="CellClientOptions.TokenRefreshMargin"/> before it expires. A <c>401</c> from the cell drops the token, and the
/// call is sent once more with a new one. Every <c>POST</c> carries the <c>Idempotency-Key</c> of its
/// <see cref="CellOperation"/>. A failure throws <see cref="CellCallException"/> with a fixed text: it never holds a
/// response body, a token, the client secret or a request body, and none of them is logged.
/// </summary>
internal sealed class CellClient : IDisposable
{
    private readonly CellOptions _cell;
    private readonly string _clientSecret;
    private readonly Func<HttpClient> _httpClient;
    private readonly CellClientOptions _options;
    private readonly TimeProvider _clock;
    private readonly Uri _baseUri;
    private readonly Uri _tokenUri;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenRefreshAt;

    public CellClient(CellOptions cell, string clientSecret, Func<HttpClient> httpClient, CellClientOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _cell = cell;
        _clientSecret = clientSecret;
        _httpClient = httpClient;
        _options = options;
        _clock = clock;
        CellId = cell.Id ?? throw new ArgumentException("The cell has no id.", nameof(cell));
        _baseUri = HttpsUri(cell.InternalUrl);
        _tokenUri = HttpsUri(cell.TokenUrl);
    }

    public string CellId { get; }

    public void Dispose() => _tokenLock.Dispose();

    /// <summary>
    /// Sends a <c>POST</c> that changes a tenant and returns the queued job. <paramref name="path"/> is under
    /// <c>/api/cell/</c>. A retry with the same <paramref name="operation"/> and the same body gets the first job again.
    /// </summary>
    public async Task<CellJob> PostAsync(string path, CellOperation operation, object? body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var content = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions.Web);
        return await SendForJobAsync(HttpMethod.Post, path, operation.IdempotencyKey, content, cancellationToken);
    }

    /// <summary>Reads a job with its steps and its error.</summary>
    public Task<CellJob> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        SendForJobAsync(HttpMethod.Get, $"/api/cell/jobs/{id:D}", null, null, cancellationToken);

    /// <summary>
    /// Polls a job until it is <c>succeeded</c> or <c>failed</c>, and returns it with its steps and error. The wait starts at
    /// <see cref="CellClientOptions.PollDelay"/> and doubles up to <see cref="CellClientOptions.MaxPollDelay"/>.
    /// </summary>
    /// <exception cref="TimeoutException">The job did not end within <see cref="CellClientOptions.FollowTimeout"/>.</exception>
    public async Task<CellJob> FollowAsync(Guid id, CancellationToken cancellationToken)
    {
        var start = _clock.GetUtcNow();
        var delay = _options.PollDelay;
        while (true)
        {
            var job = await GetJobAsync(id, cancellationToken);
            if (job.Ended)
            {
                return job;
            }

            if (job.State is not (CellJob.Queued or CellJob.Running))
            {
                throw new CellCallException($"The cell '{CellId}' answered the job {id:D} with a state this client does not know.", null);
            }

            if (_clock.GetUtcNow() - start >= _options.FollowTimeout)
            {
                throw new TimeoutException($"Following the job {id:D} of the cell '{CellId}' took longer than {_options.FollowTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes.");
            }

            await Task.Delay(delay, _clock, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxPollDelay.Ticks));
        }
    }

    private async Task<CellJob> SendForJobAsync(HttpMethod method, string path, string? idempotencyKey, byte[]? content, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, path);
        var call = $"{method.Method} {uri.AbsolutePath}";
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Authority != _baseUri.Authority)
        {
            throw new CellCallException($"The call {call} to the cell '{CellId}' must go to the cell's https address.", null);
        }

        var token = await GetTokenAsync(cancellationToken);
        var response = await SendAsync(method, uri, call, token, idempotencyKey, content, cancellationToken);
        try
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                await DropTokenAsync(token, cancellationToken);
                token = await GetTokenAsync(cancellationToken);
                response = await SendAsync(method, uri, call, token, idempotencyKey, content, cancellationToken);
            }

            return await ReadJobAsync(response, method, call, cancellationToken);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, Uri uri, string call, string token, string? idempotencyKey, byte[]? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (content is not null)
        {
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        try
        {
            return await _httpClient().SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // The exception message can hold a value, so the message names only the exception type.
            throw new CellCallException($"The call {call} to the cell '{CellId}' failed: {exception.GetType().FullName}.", null, exception);
        }
    }

    private async Task<CellJob> ReadJobAsync(HttpResponseMessage response, HttpMethod method, string call, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var accepted = method == HttpMethod.Post
            ? response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK
            : response.StatusCode == HttpStatusCode.OK;
        if (!accepted)
        {
            throw new CellCallException($"The call {call} to the cell '{CellId}' answered with the status {status.ToString(CultureInfo.InvariantCulture)}.", status);
        }

        try
        {
            var job = await response.Content.ReadFromJsonAsync<CellJob>(JsonSerializerOptions.Web, cancellationToken);
            if (job is not null && !string.IsNullOrEmpty(job.State))
            {
                return job;
            }
        }
        catch (JsonException)
        {
            // The body is not a job, which the fixed text below says without quoting it.
        }

        throw new CellCallException($"The call {call} to the cell '{CellId}' answered with a body that is not a job.", status);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _clock.GetUtcNow() < _tokenRefreshAt)
            {
                return _token;
            }

            var (token, expiresIn) = await RequestTokenAsync(cancellationToken);
            if (expiresIn > _options.TokenRefreshMargin)
            {
                _token = token;
                _tokenRefreshAt = _clock.GetUtcNow() + expiresIn.Value - _options.TokenRefreshMargin;
            }
            else
            {
                // Without a lifetime, or with one inside the margin, the token serves the one call and is not kept.
                _token = null;
            }

            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Forgets <paramref name="used"/>, unless a newer token has replaced it already.</summary>
    private async Task DropTokenAsync(string used, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token == used)
            {
                _token = null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<(string Token, TimeSpan? ExpiresIn)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        // Both are form-encoded in the Basic credentials, as RFC 6749 section 2.3.1 says.
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{WebUtility.UrlEncode(_cell.ClientId)}:{WebUtility.UrlEncode(_clientSecret)}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUri)
        {
            Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient().SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new CellCallException($"The token request for the cell '{CellId}' failed: {exception.GetType().FullName}.", null, exception);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                throw new CellCallException($"The token request for the cell '{CellId}' answered with the status {status.ToString(CultureInfo.InvariantCulture)}.", status);
            }

            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("access_token", out var token)
                    && token.ValueKind == JsonValueKind.String
                    && token.GetString() is { Length: > 0 } text)
                {
                    TimeSpan? expiresIn = root.TryGetProperty("expires_in", out var seconds)
                        && seconds.ValueKind == JsonValueKind.Number
                        && seconds.TryGetDouble(out var value)
                        && value is > 0 and < 10_000_000_000
                            ? TimeSpan.FromSeconds(value)
                            : null;
                    return (text, expiresIn);
                }
            }
            catch (JsonException)
            {
                // The body is not JSON, which the fixed text below says without quoting it.
            }

            throw new CellCallException($"The token request for the cell '{CellId}' answered with a body that holds no access token.", status);
        }
    }

    private static Uri HttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new ArgumentException("A cell address must be an absolute https URL.", nameof(value));
}
