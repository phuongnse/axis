namespace Axis.Processes.Instances;

/// <summary>
/// The one HTTP client that sends connector calls. It follows no redirect and has no timeout of its
/// own, because each call is cancelled after its connector's timeout. It logs no request or header.
/// </summary>
internal sealed class ConnectorClient : IDisposable
{
    /// <summary>The request option that holds the connector name. Only scenario stubs read it.</summary>
    internal static readonly HttpRequestOptionsKey<string> ConnectorOption = new("axis.connector");

    /// <summary>The request option that holds the operation name. Only scenario stubs read it.</summary>
    internal static readonly HttpRequestOptionsKey<string> OperationOption = new("axis.operation");

    private readonly HttpClient _client;

    public ConnectorClient()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
    }

    /// <summary>Sends through <paramref name="handler"/> instead of a socket, so a scenario can answer from its stubs.</summary>
    internal ConnectorClient(HttpMessageHandler handler)
    {
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Sends <paramref name="request"/> and reads the whole response.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

    public void Dispose() => _client.Dispose();
}
