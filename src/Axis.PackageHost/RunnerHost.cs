using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Logging;

namespace Axis.PackageHost;

/// <summary>
/// Host mode: accepts TCP connections and starts one runner process for each, see
/// docs/reference/extension-packages.md#the-runner-protocol. The first line of a connection must be a
/// <c>start</c> for an installed version whose id and version are well formed. Any other first line, or a
/// version that is not installed, closes the connection and starts no runner. The runner gets an empty
/// environment, its standard input is fed from the connection and its standard output goes to the
/// connection. Closing the connection kills the runner and its child processes, and a runner that exits
/// closes the connection. The host logs the runner's process id, package and version, and no message.
/// </summary>
internal sealed partial class RunnerHost(string extensionsRoot, RunnerCommand command, ILogger<RunnerHost> logger) : IAsyncDisposable
{
    /// <summary>The longest <c>start</c> line the host reads, in bytes.</summary>
    private const int MaxStartLine = 4096;

    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private TcpListener? _listener;
    private Task? _accepting;
    private int _nextConnection;

    /// <summary>Starts listening on <paramref name="bind"/> and returns the endpoint it is bound to.</summary>
    public Task<IPEndPoint> StartAsync(IPEndPoint bind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bind);
        cancellationToken.ThrowIfCancellationRequested();

        var listener = new TcpListener(bind);
        listener.Start();
        _listener = listener;
        _accepting = AcceptAsync(listener);
        return Task.FromResult((IPEndPoint)listener.LocalEndpoint);
    }

    /// <summary>Stops listening, kills every runner and waits until their connections are closed.</summary>
    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        _listener?.Stop();
        if (_accepting is not null)
        {
            await _accepting;
        }

        await Task.WhenAll(_connections.Values);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stopping.Dispose();
    }

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
            await ServeAsync(client);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            // The peer went away. Its runner is already killed.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var reader = PipeReader.Create(client.GetStream());
            try
            {
                if (await ReadStartAsync(reader) is not { } start)
                {
                    return;
                }

                using var runner = StartRunner(start);
                LogStarted(runner.Id, start.Package, start.Version);
                await PipeAsync(reader, client.GetStream(), runner);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }

    /// <summary>
    /// Reads the first line. Returns the <c>start</c> it holds, or <see langword="null"/> when the connection ends
    /// first or the line is too long, is not a <c>start</c>, names an id or version that is not well formed, or names a
    /// version that is not installed. Bytes after the line stay in the reader.
    /// </summary>
    private async Task<StartMessage?> ReadStartAsync(PipeReader reader)
    {
        while (true)
        {
            var result = await reader.ReadAsync(_stopping.Token);
            var buffer = result.Buffer;
            var end = buffer.PositionOf((byte)'\n');
            if (end is { } position)
            {
                var line = Encoding.UTF8.GetString(buffer.Slice(0, position));
                reader.AdvanceTo(buffer.GetPosition(1, position));
                return Parse(line);
            }

            if (result.IsCompleted || buffer.Length > MaxStartLine)
            {
                reader.AdvanceTo(buffer.End);
                return null;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private StartMessage? Parse(string line)
    {
        try
        {
            return RunnerProtocol.Read(line) is StartMessage start
                && IdPattern().IsMatch(start.Package)
                && VersionPattern().IsMatch(start.Version)
                && File.Exists(Path.Combine(extensionsRoot, start.Package, start.Version, PackageCatalog.ManifestFileName))
                    ? start
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Process StartRunner(StartMessage start)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.ArgumentList.Add("runner");
        info.ArgumentList.Add(extensionsRoot);
        info.ArgumentList.Add(start.Package);
        info.ArgumentList.Add(start.Version);
        info.Environment.Clear();
        return Process.Start(info) ?? throw new InvalidOperationException("The runner did not start.");
    }

    /// <summary>
    /// Copies the connection to the runner's standard input and its standard output to the connection, until one
    /// side ends. The caller then kills the runner, so a runner that exits closes the connection and a closed
    /// connection kills the runner.
    /// </summary>
    private async Task PipeAsync(PipeReader fromSocket, Stream socket, Process runner)
    {
        var toRunner = fromSocket.CopyToAsync(runner.StandardInput.BaseStream, _stopping.Token);
        var fromRunner = runner.StandardOutput.BaseStream.CopyToAsync(socket, _stopping.Token);
        try
        {
            await Task.WhenAny(toRunner, fromRunner);
        }
        finally
        {
            // Killing the runner ends both copies, and closing the socket ends the one that waits on it.
            await KillAsync(runner);
            socket.Close();
            await Task.WhenAll(Settled(toRunner), Settled(fromRunner));
        }
    }

    private static async Task KillAsync(Process runner)
    {
        try
        {
            runner.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The runner has already exited.
        }

        await runner.WaitForExitAsync();
    }

    /// <summary>Waits for a copy to end. A copy that fails because its stream was closed has ended too.</summary>
    private static async Task Settled(Task copy)
    {
        try
        {
            await copy;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            // The stream was closed under the copy.
        }
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9-]*\z")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")]
    private static partial Regex VersionPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {ProcessId} started for package {Package} {Version}.")]
    private partial void LogStarted(int processId, string package, string version);
}
