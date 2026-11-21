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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.PackageHost;

/// <summary>
/// Host mode: accepts TCP connections and starts one runner process for each, see
/// docs/reference/package-runners.md#the-runner-protocol. The first line of a connection must be a
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
/// the files of the built copy, without symbolic links, or <c>refused</c> with each problem, then closes the connection. A problem names its
/// <c>line</c> and <c>column</c> when it has a place in its file, and both answers carry <c>output</c>, the lines of the steps that passed
/// and, for <c>refused</c>, each problem's message. It
/// writes nothing under the extensions folder, starts no runner and deletes its copies whatever the answer. A path that is not
/// a relative path inside the package is refused before anything is written. A build that cannot run is logged, and the
/// connection closes with no answer.
/// The first line may also be a <c>test</c>, which is as long as a <c>build</c> may be. The host builds the package as for a <c>build</c>,
/// then runs the tests of a Python package in the built copy with <see cref="PackageTests"/>, and answers <c>tested</c> with the outcome of each test,
/// or <c>refused</c> when the build fails, the package is not a Python package, or the tests do not finish in time. It deletes its copies whatever the answer.
/// For a runner started from files the host reads the lines both ways. It measures the runner process's own CPU time from each line to the
/// runner and sends a <c>usage</c> line right after each <c>result</c>, <c>retryable</c> or <c>failure</c> that the runner writes, and it
/// drops a <c>usage</c> line that the runner writes itself. A runner of an installed version gets the raw copy and no <c>usage</c> line.
/// </summary>
internal sealed partial class RunnerHost(
    string extensionsRoot, RunnerCommands commands, ILogger<RunnerHost> logger, Sandbox? sandbox = null, IConfiguration? configuration = null, TimeSpan? testLimit = null) : IAsyncDisposable
{
    /// <summary>The longest line the host reads as the first line of a connection, other than a <c>build</c> or a <c>start</c> with files, in bytes.</summary>
    private const int MaxStartLine = 4096;

    /// <summary>The longest <c>build</c> line the host reads, in bytes.</summary>
    private const int MaxBuildLine = 64 * 1024 * 1024;

    private readonly Sandbox _sandbox = sandbox ?? Sandbox.ForThisMachine();
    private readonly IConfiguration _configuration = configuration ?? new ConfigurationBuilder().Build();
    private readonly TimeSpan _testLimit = testLimit ?? TimeSpan.FromMinutes(2);
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

                if (started.Test is not null)
                {
                    await TestAsync(started.Test, client.GetStream());
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
    /// Runs a <c>start</c> with files, see docs/reference/package-runners.md#the-runner-protocol. A bad path or a manifest that names
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

        await PipeAsync(reader, socket, runner, reportUsage: tenant is not null);
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
    /// What the first line asked for: a <see cref="Start"/> with the language of its installed manifest, or <c>default</c> for a start with files, a <see cref="Build"/>, a <see cref="Test"/>, or, with none of
    /// them, a <c>sandbox</c> question.
    /// </summary>
    private sealed record FirstLine(StartMessage? Start, PackageLanguage Language, BuildMessage? Build, TestMessage? Test = null);

    /// <summary>
    /// Reads the first line. Returns the <c>start</c> it holds with the language of its manifest, the <c>build</c> or <c>test</c> it holds, or a
    /// <c>FirstLine</c> with none of them for a <c>sandbox</c> question. Returns <see langword="null"/> when the connection ends
    /// first or the line is too long, is none of these, names an id or version that is not well formed, or names a
    /// version that is not installed. A <c>start</c> with files is not looked up and needs a tenant. Only a <c>build</c>, a <c>test</c> and a <c>start</c> with files may be longer than <see cref="MaxStartLine"/>. Bytes after the line stay in the reader.
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

            if (message is TestMessage test)
            {
                return new FirstLine(null, default, null, test);
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
    private Task BuildAsync(BuildMessage build, Stream socket) =>
        BuildCopyAsync(build.Files, socket, async outcome =>
        {
            var built = await ReadFilesAsync(outcome.Folder!);
            await RunnerProtocol.WriteAsync(socket, new BuiltMessage(built, outcome.Output), _stopping.Token);
            LogBuilt(outcome.Manifest!.Id, outcome.Manifest.Version);
        });

    /// <summary>
    /// Builds the package in <paramref name="test"/> as for a <c>build</c>, then runs the tests of a Python package in the built copy
    /// and answers <c>tested</c>. A package of another language, and tests that run too long, are refused at the manifest. A run that
    /// cannot start is logged and gets no answer.
    /// </summary>
    private Task TestAsync(TestMessage test, Stream socket) =>
        BuildCopyAsync(test.Files, socket, async outcome =>
        {
            var manifest = outcome.Manifest!;
            if (manifest.Language != PackageLanguage.Python)
            {
                var language = manifest.Language == PackageLanguage.Node ? "node" : "csharp";
                await AnswerRefusedAsync(socket, [new BuildProblem(PackageCatalog.ManifestFileName, $"The tests of a '{language}' package do not run yet. Only a Python package's tests run.")], outcome.Output);
                return;
            }

            var testFiles = test.Files.Keys.Where(path => TestFilePattern().IsMatch(path.Split('/')[^1]));
            var run = await PackageTests.RunAsync(_configuration, outcome.Folder!, testFiles, _testLimit, _stopping.Token);
            if (run.SetupError is not null)
            {
                LogBuildNotRun(run.SetupError);
                return;
            }

            if (run.Refusal is not null)
            {
                await AnswerRefusedAsync(socket, [new BuildProblem(PackageCatalog.ManifestFileName, run.Refusal)], outcome.Output);
                return;
            }

            await RunnerProtocol.WriteAsync(socket, new TestedMessage(run.Tests!), _stopping.Token);
            LogTested(manifest.Id, manifest.Version);
        });

    /// <summary>
    /// Writes <paramref name="files"/> into a temporary copy, runs the checks of <see cref="PackageChecks"/> on it and, when they pass, calls
    /// <paramref name="answerBuilt"/> with the outcome, whose folder holds the built package. Answers <c>refused</c> for a path that is not
    /// inside the package or a problem of the checks, and logs a setup error with no answer. It deletes its copies whatever happens.
    /// </summary>
    private async Task BuildCopyAsync(Dictionary<string, byte[]> files, Stream socket, Func<PackageChecks.Outcome, Task> answerBuilt)
    {
        if (BadPaths(files.Keys) is { Count: > 0 } badPaths)
        {
            await AnswerRefusedAsync(socket, badPaths, []);
            return;
        }

        var copy = Directory.CreateTempSubdirectory("axis-package-build-").FullName;
        try
        {
            if (!Write(files, copy, out var unwritable))
            {
                await AnswerRefusedAsync(socket, unwritable, []);
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
                await AnswerRefusedAsync(
                    socket,
                    [.. outcome.Problems.Select(problem => new BuildProblem(problem.File, problem.Message, problem.Line, problem.Column))],
                    outcome.Output);
                return;
            }

            await answerBuilt(outcome);
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

    /// <summary>Answers <c>refused</c>, with <paramref name="steps"/>, the lines of the steps that passed, and then each problem's message as its <c>output</c>.</summary>
    private async Task AnswerRefusedAsync(Stream socket, List<BuildProblem> problems, IEnumerable<string> steps)
    {
        await RunnerProtocol.WriteAsync(socket, new RefusedMessage(problems, [.. steps, .. problems.Select(problem => problem.Message)]), _stopping.Token);
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
    /// Starts the runner of <paramref name="language"/> with the worker's egress proxy as its one route to the network, whose credentials
    /// are the runner's token. No NO_PROXY is set, so a request to a loopback address goes through the proxy too. Node.js makes its own
    /// HTTP calls through these variables only when it is told to. Returns <see langword="null"/> when the language has no runner or the
    /// runner's program is not installed, so that no runner starts.
    /// </summary>
    private Process? TryStartRunner(string root, StartMessage start, PackageLanguage language)
    {
        var proxy = $"http://axis:{start.ProxyToken}@{start.Proxy}";
        var environment = new Dictionary<string, string>();
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
        {
            environment[name] = proxy;
        }

        if (language == PackageLanguage.Node)
        {
            environment["NODE_USE_ENV_PROXY"] = "1";
        }

        return RunnerProcess.TryStart(commands, root, start.Package, start.Version, language, environment);
    }

    /// <summary>
    /// Copies the connection to the runner's standard input and its standard output to the connection, until one
    /// side ends. The caller then kills the runner, so a runner that exits closes the connection and a closed
    /// connection kills the runner. With <paramref name="reportUsage"/>, which is set for a runner started from files, the copies work on lines.
    /// The host notes the runner's CPU time when a line goes to the runner and sends a <c>usage</c> line after each answer that comes from it,
    /// see docs/reference/package-runners.md#the-runner-protocol. A <c>usage</c> line from the runner is dropped.
    /// </summary>
    private async Task PipeAsync(PipeReader fromSocket, Stream socket, Process runner, bool reportUsage)
    {
        Task toRunner;
        Task fromRunner;
        PipeReader? runnerOutput = null;
        if (reportUsage)
        {
            var cpu = new CpuClock(runner);
            runnerOutput = PipeReader.Create(runner.StandardOutput.BaseStream);
            toRunner = CopyLinesAsync(fromSocket, async (line, token) =>
            {
                cpu.Mark();
                await WriteLineAsync(runner.StandardInput.BaseStream, line, token);
            });
            fromRunner = CopyLinesAsync(runnerOutput, (line, token) => AnswerLineAsync(line, socket, cpu, token));
        }
        else
        {
            toRunner = fromSocket.CopyToAsync(runner.StandardInput.BaseStream, _stopping.Token);
            fromRunner = runner.StandardOutput.BaseStream.CopyToAsync(socket, _stopping.Token);
        }

        try
        {
            await Task.WhenAny(toRunner, fromRunner);
        }
        finally
        {
            // Killing the runner ends both copies, and closing the socket ends the one that waits on it.
            await RunnerProcess.KillAsync(runner);
            socket.Close();
            await Task.WhenAll(Settled(toRunner), Settled(fromRunner));
            if (runnerOutput is not null)
            {
                await runnerOutput.CompleteAsync();
            }
        }
    }

    /// <summary>Calls <paramref name="onLine"/> with each line of <paramref name="reader"/>, without its line break, until the reader ends. A last line with no line break counts as a line.</summary>
    private async Task CopyLinesAsync(PipeReader reader, Func<ReadOnlySequence<byte>, CancellationToken, ValueTask> onLine)
    {
        // The bytes that hold no line break yet are not searched again, so a long line costs one pass.
        long searched = 0;
        while (true)
        {
            var result = await reader.ReadAsync(_stopping.Token);
            var buffer = result.Buffer;
            while (buffer.Slice(searched).PositionOf((byte)'\n') is { } end)
            {
                await onLine(buffer.Slice(0, end), _stopping.Token);
                buffer = buffer.Slice(buffer.GetPosition(1, end));
                searched = 0;
            }

            if (result.IsCompleted)
            {
                if (!buffer.IsEmpty)
                {
                    await onLine(buffer, _stopping.Token);
                }

                reader.AdvanceTo(result.Buffer.End);
                return;
            }

            searched = buffer.Length;
            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// Passes one line of the runner's standard output on to the connection. After a <c>result</c>, a <c>retryable</c> or a <c>failure</c> it
    /// sends <c>usage</c> with the runner's CPU time since the last line went to it. A <c>usage</c> line from the runner is dropped, because only
    /// the host may write one. A runner that has exited gets no <c>usage</c> line.
    /// </summary>
    private static async ValueTask AnswerLineAsync(ReadOnlySequence<byte> line, Stream socket, CpuClock cpu, CancellationToken cancellationToken)
    {
        RunnerMessage? message = null;
        try
        {
            message = RunnerProtocol.Read(line);
        }
        catch (JsonException)
        {
            // A line that is not a message goes through unchanged.
        }

        if (message is UsageMessage)
        {
            return;
        }

        await WriteLineAsync(socket, line, cancellationToken);
        if (message is ResultMessage or RetryableMessage or FailureMessage && cpu.Since() is { } used)
        {
            await RunnerProtocol.WriteAsync(socket, new UsageMessage((long)Math.Ceiling(used.TotalMilliseconds)), cancellationToken);
        }
    }

    private static async ValueTask WriteLineAsync(Stream stream, ReadOnlySequence<byte> line, CancellationToken cancellationToken)
    {
        foreach (var segment in line)
        {
            await stream.WriteAsync(segment, cancellationToken);
        }

        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// The CPU time of one runner process, which is its own user and system time and not that of the processes it starts. <see cref="Mark"/>
    /// notes the time when a line goes to the runner, and <see cref="Since"/> reads what the runner used after it. The marks and the reads come from
    /// the two copies of one connection.
    /// </summary>
    private sealed class CpuClock(Process runner)
    {
        private long _marked = Read(runner)?.Ticks ?? 0;

        public void Mark()
        {
            if (Read(runner) is { } now)
            {
                Interlocked.Exchange(ref _marked, now.Ticks);
            }
        }

        /// <summary>The CPU time since the last mark, or <see langword="null"/> when the runner has exited and cannot be read.</summary>
        public TimeSpan? Since() =>
            Read(runner) is { } now ? TimeSpan.FromTicks(Math.Max(0, now.Ticks - Interlocked.Read(ref _marked))) : null;

        private static TimeSpan? Read(Process runner)
        {
            try
            {
                return runner.TotalProcessorTime;
            }
            catch (InvalidOperationException)
            {
                // The runner has exited.
                return null;
            }
        }
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

    [GeneratedRegex(@"\Atest_.*\.py\z")]
    private static partial Regex TestFilePattern();

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")]
    private static partial Regex VersionPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered a sandbox question: gVisor {Gvisor}.")]
    private partial void LogSandboxAnswered(bool gvisor);

    [LoggerMessage(Level = LogLevel.Information, Message = "Built package {Package} {Version}.")]
    private partial void LogBuilt(string package, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tested package {Package} {Version}.")]
    private partial void LogTested(string package, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused a package build with {Count} problems.")]
    private partial void LogBuildRefused(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A package build could not run: {Reason}")]
    private partial void LogBuildNotRun(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {ProcessId} started for package {Package} {Version}.")]
    private partial void LogStarted(int processId, string package, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {ProcessId} started for tenant {Tenant}, package {Package} {Version}.")]
    private partial void LogStartedForTenant(int processId, string tenant, string package, string version);
}
