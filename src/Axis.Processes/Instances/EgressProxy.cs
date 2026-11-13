using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Axis.Processes.Instances;

/// <summary>
/// The worker's egress proxy, the one network route of a package runner, see
/// docs/reference/extension-packages.md#package-runners. Each runner has its own token, which it sends as the proxy
/// credentials. For the call that a runner is running, the proxy admits only the scheme, host and port of the
/// <c>url</c> bindings the call declares, and plain <c>http</c> only when it was built to allow it. A runner with no
/// call reaches nothing. A request that is not admitted gets <c>403</c> and marks the runner's call refused, and a
/// request with no known token gets <c>407</c>. The proxy follows no redirect, so a redirect to another host is a new
/// request that it checks again. It forwards an <c>http</c> request on a connection of its own and ends it after
/// the response, and it pipes the bytes of a <c>CONNECT</c> tunnel without reading them. It logs no host and no URL.
/// </summary>
internal sealed class EgressProxy(int port, bool allowHttp) : IAsyncDisposable
{
    private const int MaxHead = 16 * 1024;

    private static readonly TimeSpan _readTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(1);
    private static readonly byte[] _headEnd = "\r\n\r\n"u8.ToArray();

    private readonly ConcurrentDictionary<string, Grant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _start = new();
    private TcpListener? _listener;
    private Task? _accepting;
    private int _nextConnection;

    /// <summary>The port the proxy is bound to, or the port it was built with while it is not started.</summary>
    public int Port => _listener is { } listener ? ((IPEndPoint)listener.LocalEndpoint).Port : port;

    /// <summary>Starts listening on every interface of the worker. Calling it again does nothing.</summary>
    public void EnsureStarted()
    {
        lock (_start)
        {
            if (_listener is not null)
            {
                return;
            }

            var listener = Socket.OSSupportsIPv6 ? TcpListener.Create(port) : new TcpListener(IPAddress.Any, port);
            listener.Start();
            _listener = listener;
            _accepting = AcceptAsync(listener);
        }
    }

    /// <summary>Makes the token of a new runner, which admits nothing until <see cref="Admit"/>.</summary>
    public string Register()
    {
        var token = Guid.NewGuid().ToString("N");
        _grants[token] = new Grant();
        return token;
    }

    /// <summary>Admits the scheme, host and port of <paramref name="urls"/> for the runner's call and clears its refusal.</summary>
    public void Admit(string token, IEnumerable<Uri> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);

        if (_grants.TryGetValue(token, out var grant))
        {
            grant.Begin([.. urls.Select(url => (url.Scheme, Normalize(url.IdnHost), url.Port))]);
        }
    }

    /// <summary>Admits nothing for the runner any more. Returns whether the proxy refused a request of the runner since <see cref="Admit"/>.</summary>
    public bool EndCall(string token) => _grants.TryGetValue(token, out var grant) && grant.End();

    /// <summary>Forgets the token of a runner that is gone.</summary>
    public void Remove(string token) => _grants.TryRemove(token, out _);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener?.Stop();
        if (_accepting is not null)
        {
            await _accepting;
        }

        await Task.WhenAll(_connections.Values);
        _stopping.Dispose();
    }

    private static string Normalize(string host) => host.Trim('[', ']').ToLowerInvariant();

    private async Task AcceptAsync(TcpListener listener)
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            var id = Interlocked.Increment(ref _nextConnection);
            var serving = Task.Run(() => ServeQuietlyAsync(client));
            _connections[id] = serving;
            _ = serving.ContinueWith(_ => _connections.TryRemove(id, out Task? _), TaskScheduler.Default);
        }
    }

    private async Task ServeQuietlyAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                await ServeAsync(client);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException or InvalidOperationException)
        {
            // The peer or the upstream went away.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var read = await ReadHeadAsync(stream);
        if (read is not { } head)
        {
            return;
        }

        var lines = Encoding.Latin1.GetString(head.Head).Split("\r\n");
        var credentials = lines.Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(parts => TokenOf(parts[1].Trim()))
            .FirstOrDefault();
        if (credentials is null || !_grants.TryGetValue(credentials, out var grant))
        {
            await AnswerAsync(client, "407 Proxy Authentication Required", "Proxy-Authenticate: Basic realm=\"axis\"\r\n");
            return;
        }

        var request = lines[0].Split(' ');
        if (request is [var method, var target, var version] && version.StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseAuthority(target, out var host, out var tunnelPort) && grant.Admits("https", host, tunnelPort))
                {
                    await TunnelAsync(client, host, tunnelPort, head.Remainder);
                    return;
                }
            }
            else if (allowHttp
                && Uri.TryCreate(target, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttp
                && grant.Admits(uri.Scheme, Normalize(uri.IdnHost), uri.Port))
            {
                await ForwardAsync(client, method, uri, lines.Skip(1), head.Remainder);
                return;
            }
        }

        grant.Refuse();
        await AnswerAsync(client, "403 Forbidden", "");
    }

    /// <summary>Reads up to the end of the request head. Returns <see langword="null"/> for a head that is too long, late or cut off.</summary>
    private async Task<(byte[] Head, byte[] Remainder)?> ReadHeadAsync(NetworkStream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        timeout.CancelAfter(_readTimeout);
        var buffer = new byte[MaxHead];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
                if (count == 0)
                {
                    return null;
                }

                var from = Math.Max(0, length - (_headEnd.Length - 1));
                length += count;
                var end = buffer.AsSpan(from, length - from).IndexOf(_headEnd);
                if (end >= 0)
                {
                    var headLength = from + end;
                    return (buffer[..headLength], buffer[(headLength + _headEnd.Length)..length]);
                }
            }
        }
        catch (OperationCanceledException) when (!_stopping.IsCancellationRequested)
        {
            // The head took too long.
        }

        return null;
    }

    /// <summary>The token in the <c>Basic</c> credentials of a <c>Proxy-Authorization</c> value, or <see langword="null"/>.</summary>
    private static string? TokenOf(string value)
    {
        const string Scheme = "Basic ";
        if (!value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = new byte[value.Length];
        if (!Convert.TryFromBase64String(value[Scheme.Length..].Trim(), bytes, out var written))
        {
            return null;
        }

        var pair = Encoding.UTF8.GetString(bytes, 0, written);
        var separator = pair.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? null : pair[(separator + 1)..];
    }

    /// <summary>Parses the <c>host:port</c> of a <c>CONNECT</c> target, where an IPv6 host is in brackets.</summary>
    private static bool TryParseAuthority(string target, out string host, out int port)
    {
        var separator = target.LastIndexOf(':');
        host = separator > 0 ? Normalize(target[..separator]) : string.Empty;
        port = 0;
        return host.Length > 0 && int.TryParse(target[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port);
    }

    private async Task TunnelAsync(TcpClient client, string host, int tunnelPort, byte[] rest)
    {
        using var upstream = await ConnectAsync(host, tunnelPort);
        if (upstream is null)
        {
            await AnswerAsync(client, "502 Bad Gateway", "");
            return;
        }

        var stream = client.GetStream();
        await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), _stopping.Token);
        var toUpstream = upstream.GetStream();
        await toUpstream.WriteAsync(rest, _stopping.Token);
        await Task.WhenAll(
            PipeAsync(stream, toUpstream, client.Client, upstream.Client),
            PipeAsync(toUpstream, stream, upstream.Client, client.Client));
    }

    private async Task ForwardAsync(TcpClient client, string method, Uri uri, IEnumerable<string> headers, byte[] rest)
    {
        using var upstream = await ConnectAsync(uri.IdnHost, uri.Port);
        if (upstream is null)
        {
            await AnswerAsync(client, "502 Bad Gateway", "");
            return;
        }

        var head = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{method} {uri.PathAndQuery} HTTP/1.1\r\n");
        foreach (var header in headers)
        {
            var name = header.Split(':', 2)[0].Trim();
            if (!name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                head.Append(header).Append("\r\n");
            }
        }

        head.Append("Connection: close\r\nVia: 1.1 axis-egress-proxy\r\n\r\n");
        var toUpstream = upstream.GetStream();
        await toUpstream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), _stopping.Token);
        await toUpstream.WriteAsync(rest, _stopping.Token);

        // The connection ends with the response, so the proxy never reads a second request on it.
        var stream = client.GetStream();
        var fromClient = PipeAsync(stream, toUpstream, client.Client, upstream.Client);
        await PipeAsync(toUpstream, stream, upstream.Client, client.Client);
        client.Client.Close();
        await fromClient;
    }

    private async Task<TcpClient?> ConnectAsync(string host, int connectPort)
    {
        var upstream = new TcpClient { NoDelay = true };
        try
        {
            await upstream.ConnectAsync(host, connectPort, _stopping.Token);
            return upstream;
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            upstream.Dispose();
            return null;
        }
    }

    /// <summary>Copies one direction until the source ends, then ends the destination's sending side.</summary>
    private async Task PipeAsync(NetworkStream from, NetworkStream to, Socket source, Socket destination)
    {
        try
        {
            await from.CopyToAsync(to, _stopping.Token);
            destination.Shutdown(SocketShutdown.Send);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // One side closed under the copy, which ends the other one.
            source.Close();
            destination.Close();
        }
    }

    /// <summary>
    /// Answers with a status line and no body, then closes. It reads what the peer still sends first, because
    /// closing a socket with unread data resets it and the peer may lose the answer.
    /// </summary>
    private async Task AnswerAsync(TcpClient client, string status, string headers)
    {
        var stream = client.GetStream();
        var response = Encoding.Latin1.GetBytes($"HTTP/1.1 {status}\r\n{headers}Connection: close\r\nContent-Length: 0\r\n\r\n");
        await stream.WriteAsync(response, _stopping.Token);
        client.Client.Shutdown(SocketShutdown.Send);
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        drain.CancelAfter(_drainTimeout);
        var buffer = new byte[1024];
        try
        {
            while (await stream.ReadAsync(buffer, drain.Token) > 0)
            {
            }
        }
        catch (OperationCanceledException) when (!_stopping.IsCancellationRequested)
        {
            // The peer did not close in time.
        }
    }

    /// <summary>What one runner may reach now, and whether the proxy refused it a request during its call.</summary>
    private sealed class Grant
    {
        private readonly Lock _lock = new();
        private HashSet<(string Scheme, string Host, int Port)> _origins = [];
        private bool _refused;

        public void Begin(HashSet<(string Scheme, string Host, int Port)> origins)
        {
            lock (_lock)
            {
                _origins = origins;
                _refused = false;
            }
        }

        public bool End()
        {
            lock (_lock)
            {
                _origins = [];
                var refused = _refused;
                _refused = false;
                return refused;
            }
        }

        public bool Admits(string scheme, string host, int port)
        {
            lock (_lock)
            {
                return _origins.Contains((scheme, host, port));
            }
        }

        public void Refuse()
        {
            lock (_lock)
            {
                _refused = true;
            }
        }
    }
}
