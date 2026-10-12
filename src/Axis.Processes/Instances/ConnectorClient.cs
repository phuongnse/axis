namespace Axis.Processes.Instances;

/// <summary>
/// The one HTTP client that sends connector calls. It follows no redirect and has no timeout of its
/// own, because each call is cancelled after its connector's timeout. It logs no request or header.
/// </summary>
internal sealed class ConnectorClient : IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>Sends <paramref name="request"/> and reads the whole response.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

    public void Dispose() => _client.Dispose();
}
