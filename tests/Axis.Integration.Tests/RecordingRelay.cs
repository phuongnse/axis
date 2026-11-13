using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Axis.Integration.Tests;

/// <summary>
/// A relay between the worker and a package host on loopback TCP. For each connection it connects to the
/// host and copies the bytes both ways, and it keeps the lines that the worker sent, so a test can read
/// what a runner received. With <c>closeAfterRun</c> it closes both sides right after it forwards a
/// <c>run</c> line, which stands in for a runner that exits during the attempt. Each wait of the relay
/// ends when its connection ends or when the relay is disposed.
/// </summary>
internal sealed class RecordingRelay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly IPEndPoint _target;
    private readonly bool _closeAfterRun;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly ConcurrentDictionary<TcpClient, bool> _clients = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Task _accepting;
    private int _connectionCount;

    public RecordingRelay(IPEndPoint target, bool closeAfterRun = false)
    {
        _target = target;
        _closeAfterRun = closeAfterRun;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>The address the worker connects to, as <c>host:port</c>.</summary>
    public string Address => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

    /// <summary>How many connections the worker opened, one for each runner it started.</summary>
    public int Connections => Volatile.Read(ref _connectionCount);

    /// <summary>The lines the worker sent, in order, over every connection.</summary>
    public IReadOnlyList<string> Lines => [.. _lines];

    /// <summary>The lines of type <paramref name="type"/> that the worker sent.</summary>
    public IReadOnlyList<string> LinesOf(string type) => [.. _lines.Where(line => line.Contains($"\"type\":\"{type}\"", StringComparison.Ordinal))];

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        foreach (var client in _clients.Keys)
        {
            client.Dispose();
        }

        await _accepting;
        await Task.WhenAll(_connections);
        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient worker;
            try
            {
                worker = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            Interlocked.Increment(ref _connectionCount);
            _connections.Add(Task.Run(() => RelayAsync(worker)));
        }
    }

    private async Task RelayAsync(TcpClient worker)
    {
        using var host = new TcpClient();
        _clients[worker] = true;
        _clients[host] = true;
        try
        {
            await host.ConnectAsync(_target, _stopping.Token);
            var toWorker = host.GetStream().CopyToAsync(worker.GetStream(), _stopping.Token);
            var toHost = CopyLinesAsync(worker, host);
            await Task.WhenAny(toWorker, toHost);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            // A side went away.
        }
        finally
        {
            worker.Dispose();
            host.Dispose();
        }
    }

    private async Task CopyLinesAsync(TcpClient from, TcpClient to)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(from.GetStream(), utf8, leaveOpen: true);
        await using var writer = new StreamWriter(to.GetStream(), utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        while (await reader.ReadLineAsync(_stopping.Token) is { } line)
        {
            _lines.Enqueue(line);
            await writer.WriteLineAsync(line.AsMemory(), _stopping.Token);
            if (_closeAfterRun && line.Contains("\"type\":\"run\"", StringComparison.Ordinal))
            {
                return;
            }
        }
    }
}
