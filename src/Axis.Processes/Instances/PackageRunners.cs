using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;

namespace Axis.Processes.Instances;

/// <summary>
/// The worker's connections to the runners of the C# package host, see
/// docs/reference/extension-packages.md#package-runners. A runner is one connection, which the host pipes to one
/// runner process, and it runs one call at a time. An idle runner waits in the pool of its package version,
/// and <see cref="RentAsync"/> starts a new one when the pool has none that is still connected. A runner that
/// exits, times out or fails is dropped, and closing its connection makes the host kill the process. Runners
/// start when an attempt needs one, so a version never has more of them than calls in flight.
/// </summary>
internal sealed class PackageRunners(string? host, int port) : IAsyncDisposable
{
    /// <summary>
    /// The longest <see cref="RentAsync"/> may take to connect and get <c>ready</c>. It is not part of an operation's
    /// timeout, which starts when the <c>run</c> is sent, so a slow start never counts as an unknown outcome.
    /// </summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string Package, string Version), ConcurrentBag<RunnerConnection>> _idle = new();
    private volatile bool _disposed;

    /// <summary>Whether <c>Extensions:Runners:Dotnet</c> names a host.</summary>
    public bool IsSet => host is not null;

    /// <summary>
    /// Takes an idle runner of the version whose connection is still open, or connects to the host, sends
    /// <c>start</c> and waits for <c>ready</c>.
    /// </summary>
    /// <exception cref="IOException">The host did not start a runner, so no <c>ready</c> came.</exception>
    /// <exception cref="SocketException">The host cannot be reached.</exception>
    public async Task<RunnerConnection> RentAsync(string package, string version, CancellationToken cancellationToken)
    {
        if (host is null)
        {
            throw new InvalidOperationException("No package host is set.");
        }

        if (_idle.TryGetValue((package, version), out var bag))
        {
            while (bag.TryTake(out var idle))
            {
                if (idle.IsOpen)
                {
                    return idle;
                }

                idle.Dispose();
            }
        }

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken);
            var runner = new RunnerConnection(client, package, version);
            try
            {
                await runner.StartAsync(cancellationToken);
                return runner;
            }
            catch
            {
                runner.Dispose();
                throw;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Keeps <paramref name="runner"/>, which finished its attempt, for the next one of its version.</summary>
    public void Return(RunnerConnection runner)
    {
        if (_disposed)
        {
            runner.Dispose();
            return;
        }

        _idle.GetOrAdd((runner.Package, runner.Version), _ => []).Add(runner);
    }

    /// <summary>Sends <c>stop</c> to each idle runner and closes it.</summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var bag in _idle.Values)
        {
            while (bag.TryTake(out var runner))
            {
                await runner.StopAsync();
                runner.Dispose();
            }
        }
    }
}

/// <summary>One connection to a runner. Lines are UTF-8 without a byte order mark, ended by <c>\n</c>.</summary>
internal sealed class RunnerConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public RunnerConnection(TcpClient client, string package, string version)
    {
        Package = package;
        Version = version;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var stream = client.GetStream();
        _client = client;
        _reader = new StreamReader(stream, utf8);
        _writer = new StreamWriter(stream, utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public string Package { get; }

    public string Version { get; }

    /// <summary>
    /// Whether the connection can still be used. A closed or reset connection reads as ready, and so does an
    /// idle runner that sent something, which the protocol does not allow.
    /// </summary>
    public bool IsOpen
    {
        get
        {
            try
            {
                return _client.Connected && !_client.Client.Poll(0, SelectMode.SelectRead);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>Sends <c>start</c> and waits for <c>ready</c>.</summary>
    /// <exception cref="IOException">The host closed the connection or answered something else.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _writer.WriteLineAsync(RunnerProtocol.Write(new StartMessage(Package, Version, Guid.NewGuid().ToString("N"))).AsMemory(), cancellationToken);
        if (await ReadAsync(cancellationToken) is not ReadyMessage)
        {
            throw new IOException("The package host did not start a runner.");
        }
    }

    /// <summary>
    /// Sends <paramref name="run"/> and reads the answer. Returns <see langword="null"/> when the runner exited,
    /// which is a closed connection, a failed write or read, or a line that is not <c>result</c>,
    /// <c>retryable</c> or <c>failure</c>.
    /// </summary>
    public async Task<RunnerMessage?> RunAsync(RunMessage run, CancellationToken cancellationToken)
    {
        try
        {
            await _writer.WriteLineAsync(RunnerProtocol.Write(run).AsMemory(), cancellationToken);
            return await ReadAsync(cancellationToken) is { } answer and (ResultMessage or RetryableMessage or FailureMessage)
                ? answer
                : null;
        }
        catch (Exception exception) when ((exception is IOException or ObjectDisposedException or SocketException) && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Tells the runner that the worker is done with it. A connection that is already closed is not an error.</summary>
    public async Task StopAsync()
    {
        try
        {
            await _writer.WriteLineAsync(RunnerProtocol.Write(new StopMessage()));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
        {
            // The runner is gone already.
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _client.Dispose();
    }

    private async Task<RunnerMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        if (await _reader.ReadLineAsync(cancellationToken) is not { } line)
        {
            return null;
        }

        try
        {
            return RunnerProtocol.Read(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
