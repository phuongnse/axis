using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.PackageHost;

/// <summary>
/// Host mode: accepts TCP connections and starts one runner process for each, see
/// docs/reference/extension-packages.md#the-runner-protocol. The first line of a connection must be a
/// <c>start</c> for an installed version whose id and version are well formed. Any other first line, or a
/// version that is not installed, or whose <c>proxy</c> is not a <c>host:port</c>, closes the connection and starts
/// no runner. So does a version whose language has no runner, or whose runner's program is missing from the image.
/// The host starts the runner of the manifest's language: the C# runner, the Node.js runner or the Python runner. The
/// runner's environment holds only the proxy variables that name the worker's egress proxy, and
/// <c>NODE_USE_ENV_PROXY=1</c> for a Node.js runner, so that its own HTTP calls use them. Its
/// standard input is fed from the connection and its standard output goes to the connection. Closing the connection
/// kills the runner and its child processes, and a runner that exits closes the connection. The host logs the
/// runner's process id, package and version, and no message. The first line may instead be a <c>sandbox</c> question with no
/// <c>gvisor</c>. The host answers it from <paramref name="sandbox"/>, closes the connection and starts no runner.
/// A <c>start</c> may carry <c>files</c> and a <c>tenant</c>. The host then writes the files to a temporary root, checks that the manifest
/// there names the <c>start</c>'s id and version, runs the runner from it, logs the tenant and deletes the root when the runner ends. A
/// bad path, a missing tenant or a manifest that names another id or version closes the connection and starts no runner.
/// The first line may also be a <c>build</c>, which may be longer than the other first lines, up to <see cref="MaxBuildLine"/> bytes.
/// So may a <c>start</c> with files.
/// The host writes its files into a temporary copy and runs the checks of <see cref="PackageChecks"/> on it, with the manifest read
/// as metadata only and with <paramref name="configuration"/> for the settings of pip and Trivy. It answers <c>built</c> with
/// the files of the built copy, without symbolic links, or <c>refused</c> with each problem, then closes the connection. It
/// writes nothing under the extensions folder, starts no runner and deletes its copies whatever the answer. A path that is not
/// a relative path inside the package is refused before anything is written. A build that cannot run is logged, and the
/// connection closes with no answer.
/// </summary>
internal sealed partial class RunnerHost(
    string extensionsRoot, RunnerCommands commands, ILogger<RunnerHost> logger, Sandbox? sandbox = null, IConfiguration? configuration = null) : IAsyncDisposable
{
    /// <summary>The longest line the host reads as the first line of a connection, other than a <c>build</c> or a <c>start</c> with files, in bytes.</summary>
    private const int MaxStartLine = 4096;

    /// <summary>The longest <c>build</c> line the host reads, in bytes.</summary>
    private const int MaxBuildLine = 64 * 1024 * 1024;

    private readonly Sandbox _sandbox = sandbox ?? Sandbox.ForThisMachine();
    private readonly IConfiguration _configuration = configuration ?? new ConfigurationBuilder().Build();
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
                if (await ReadFirstAsync(reader) is not { } started)
                {
                    return;
                }

                if (started.Build is not null)
                {
                    await BuildAsync(started.Build, client.GetStream());
                    return;
                }

                if (started.Start is null)
                {
                    await AnswerSandboxAsync(client.GetStream());
                    return;
                }

                if (started.Start.Files is not null)
                {
                    await RunFromFilesAsync(started.Start, reader, client.GetStream());
                    return;
                }

                await RunAsync(extensionsRoot, started.Start, started.Language, reader, client.GetStream(), tenant: null);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }

    /// <summary>
    /// Runs a <c>start</c> with files, see docs/reference/extension-packages.md#the-runner-protocol. A bad path or a manifest that names
    /// another id or version starts no runner. The temporary root is deleted when the runner has ended, whatever the cause.
    /// </summary>
    private async Task RunFromFilesAsync(StartMessage start, PipeReader reader, Stream socket)
    {
        if (BadPaths(start.Files!.Keys).Count > 0)
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("axis-runner-").FullName;
        try
        {
            if (!Write(start.Files, Path.Combine(root, start.Package, start.Version), out _)
                || PackageCatalog.ReadInstalledManifest(root, start.Package, start.Version) is not { } manifest
                || manifest.Id != start.Package
                || manifest.Version != start.Version)
            {
                return;
            }

            await RunAsync(root, start, manifest.Language, reader, socket, start.Tenant);
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    /// <summary>Starts the runner from <paramref name="root"/> and pipes the connection to it until one side ends.</summary>
    private async Task RunAsync(string root, StartMessage start, PackageLanguage language, PipeReader reader, Stream socket, string? tenant)
    {
        using var runner = TryStartRunner(root, start, language);
        if (runner is null)
        {
            return;
        }

        if (tenant is null)
        {
            LogStarted(runner.Id, start.Package, start.Version);
        }
        else
        {
            LogStartedForTenant(runner.Id, tenant, start.Package, start.Version);
        }

        await PipeAsync(reader, socket, runner);
    }

    /// <summary>Answers a <c>sandbox</c> question with whether this machine runs under gVisor.</summary>
    private async Task AnswerSandboxAsync(Stream socket)
    {
        var gvisor = _sandbox.IsGvisor();
        var answer = Encoding.UTF8.GetBytes(RunnerProtocol.Write(new SandboxMessage(gvisor)) + "\n");
        await socket.WriteAsync(answer, _stopping.Token);
        await socket.FlushAsync(_stopping.Token);
        LogSandboxAnswered(gvisor);
    }

    /// <summary>
    /// What the first line asked for: a <see cref="Start"/> with the language of its installed manifest, or <c>default</c> for a start with files, a <see cref="Build"/>, or, with neither, a
    /// <c>sandbox</c> question.
    /// </summary>
    private sealed record FirstLine(StartMessage? Start, PackageLanguage Language, BuildMessage? Build);

    /// <summary>
    /// Reads the first line. Returns the <c>start</c> it holds with the language of its manifest, the <c>build</c> it holds, or a
    /// <c>FirstLine</c> with neither for a <c>sandbox</c> question. Returns <see langword="null"/> when the connection ends
    /// first or the line is too long, is none of these, names an id or version that is not well formed, or names a
    /// version that is not installed. A <c>start</c> with files is not looked up and needs a tenant. Only a <c>build</c> and a <c>start</c> with files may be longer than <see cref="MaxStartLine"/>. Bytes after the line stay in the reader.
    /// </summary>
    private async Task<FirstLine?> ReadFirstAsync(PipeReader reader)
    {
        // The bytes that hold no line break yet are not searched again, so a long line costs one pass.
        long searched = 0;
        while (true)
        {
            var result = await reader.ReadAsync(_stopping.Token);
            var buffer = result.Buffer;
            var end = buffer.Slice(searched).PositionOf((byte)'\n');
            if (end is { } position)
            {
                var line = buffer.Slice(0, position);
                var first = Parse(line);
                reader.AdvanceTo(buffer.GetPosition(1, position));
                return first;
            }

            if (result.IsCompleted || buffer.Length > MaxBuildLine)
            {
                reader.AdvanceTo(buffer.End);
                return null;
            }

            searched = buffer.Length;
            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private FirstLine? Parse(ReadOnlySequence<byte> line)
    {
        try
        {
            var message = RunnerProtocol.Read(line);
            if (message is BuildMessage build)
            {
                return new FirstLine(null, default, build);
            }

            if (message is StartMessage { Files: not null } withFiles)
            {
                return IdPattern().IsMatch(withFiles.Package)
                    && VersionPattern().IsMatch(withFiles.Version)
                    && PackageIsolation.TryParseHostPort(withFiles.Proxy, out _, out _)
                    && !string.IsNullOrEmpty(withFiles.Tenant)
                        ? new FirstLine(withFiles, default, null)
                        : null;
            }

            if (line.Length > MaxStartLine)
            {
                return null;
            }

            if (message is SandboxMessage { Gvisor: null })
            {
                return new FirstLine(null, default, null);
            }

            return message is StartMessage start
                && IdPattern().IsMatch(start.Package)
                && VersionPattern().IsMatch(start.Version)
                && PackageIsolation.TryParseHostPort(start.Proxy, out _, out _)
                && PackageCatalog.ReadInstalledManifest(extensionsRoot, start.Package, start.Version) is { } manifest
                    ? new FirstLine(start, manifest.Language, null)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the package in <paramref name="build"/> as <see cref="PackageChecks"/> builds one, in a temporary copy that is deleted
    /// afterwards, and answers <c>built</c> or <c>refused</c>. A build that cannot run is logged and gets no answer.
    /// </summary>
    private async Task BuildAsync(BuildMessage build, Stream socket)
    {
        if (BadPaths(build.Files.Keys) is { Count: > 0 } badPaths)
        {
            await AnswerRefusedAsync(socket, badPaths);
            return;
        }

        var copy = Directory.CreateTempSubdirectory("axis-package-build-").FullName;
        try
        {
            if (!Write(build.Files, copy, out var unwritable))
            {
                await AnswerRefusedAsync(socket, unwritable);
                return;
            }

            using var outcome = await PackageChecks.RunAsync(_configuration, copy, PackageLoadMode.MetadataOnly, extensions: null, _stopping.Token);
            if (outcome.SetupError is not null)
            {
                LogBuildNotRun(outcome.SetupError);
                return;
            }

            if (outcome.Problems.Count > 0)
            {
                await AnswerRefusedAsync(socket, [.. outcome.Problems.Select(problem => new BuildProblem(problem.File, problem.Message))]);
                return;
            }

            var built = await ReadFilesAsync(outcome.Folder!);
            await RunnerProtocol.WriteAsync(socket, new BuiltMessage(built), _stopping.Token);
            LogBuilt(outcome.Manifest!.Id, outcome.Manifest.Version);
        }
        finally
        {
            DeleteQuietly(copy);
        }
    }

    private static void DeleteQuietly(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary folder that stays is left to the operating system.
        }
    }

    private async Task AnswerRefusedAsync(Stream socket, List<BuildProblem> problems)
    {
        await RunnerProtocol.WriteAsync(socket, new RefusedMessage(problems), _stopping.Token);
        LogBuildRefused(problems.Count);
    }

    /// <summary>One problem for each path that is not a relative path inside the package.</summary>
    private static List<BuildProblem> BadPaths(IEnumerable<string> paths) =>
        [.. paths
            .Where(path => path.Length == 0
                || Path.IsPathRooted(path)
                || path.Contains('\\')
                || path.Contains('\0')
                || path.Split('/').Any(segment => segment is "" or "." or ".."))
            .Select(path => new BuildProblem(path, $"The path '{path}' is not a relative path inside the package."))];

    /// <summary>Writes the files into <paramref name="folder"/>. Returns <see langword="false"/> with a problem for the path that cannot be written.</summary>
    private static bool Write(Dictionary<string, byte[]> files, string folder, out List<BuildProblem> problems)
    {
        problems = [];
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var (path, content) in files)
        {
            var destination = Path.GetFullPath(Path.Combine(folder, path));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                problems.Add(new BuildProblem(path, $"The path '{path}' is not a relative path inside the package."));
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, content);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The exception message names the absolute path, so it is never passed on.
                problems.Add(new BuildProblem(path, $"The file '{path}' cannot be written, for example because another file uses its name as a folder."));
                return false;
            }
        }

        return true;
    }

    /// <summary>Every file under <paramref name="folder"/>, with its path relative to it that uses <c>/</c>. Symbolic links are left out, because one could point outside the folder.</summary>
    private async Task<Dictionary<string, byte[]>> ReadFilesAsync(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = new Dictionary<string, byte[]>();
        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            var relative = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
            files[relative] = await File.ReadAllBytesAsync(file, _stopping.Token);
        }

        return files;
    }

    /// <summary>
    /// Starts the runner of <paramref name="language"/>. Returns <see langword="null"/> when the language has no runner
    /// or the runner's program is not installed, so that no runner starts.
    /// </summary>
    private Process? TryStartRunner(string root, StartMessage start, PackageLanguage language)
    {
        var command = language switch
        {
            PackageLanguage.CSharp => commands.CSharp,
            PackageLanguage.Node => commands.Node,
            PackageLanguage.Python => commands.Python,
            _ => null,
        };
        if (command is null)
        {
            return null;
        }

        try
        {
            return StartRunner(command, root, start, language);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static Process StartRunner(RunnerCommand command, string root, StartMessage start, PackageLanguage language)
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

        if (language == PackageLanguage.Python)
        {
            info.ArgumentList.Add(Path.Combine(root, start.Package, start.Version));
        }
        else
        {
            info.ArgumentList.Add("runner");
            info.ArgumentList.Add(root);
            info.ArgumentList.Add(start.Package);
            info.ArgumentList.Add(start.Version);
        }

        info.Environment.Clear();

        // The runner's one route to the network is the worker's egress proxy, whose credentials are the runner's token.
        // No NO_PROXY is set, so a request to a loopback address goes through the proxy too.
        var proxy = $"http://axis:{start.ProxyToken}@{start.Proxy}";
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
        {
            info.Environment[name] = proxy;
        }

        // Node.js makes its own HTTP calls through these variables only when it is told to.
        if (language == PackageLanguage.Node)
        {
            info.Environment["NODE_USE_ENV_PROXY"] = "1";
        }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered a sandbox question: gVisor {Gvisor}.")]
    private partial void LogSandboxAnswered(bool gvisor);

    [LoggerMessage(Level = LogLevel.Information, Message = "Built package {Package} {Version}.")]
    private partial void LogBuilt(string package, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused a package build with {Count} problems.")]
    private partial void LogBuildRefused(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A package build could not run: {Reason}")]
    private partial void LogBuildNotRun(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {ProcessId} started for package {Package} {Version}.")]
    private partial void LogStarted(int processId, string package, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {ProcessId} started for tenant {Tenant}, package {Package} {Version}.")]
    private partial void LogStartedForTenant(int processId, string tenant, string package, string version);
}
