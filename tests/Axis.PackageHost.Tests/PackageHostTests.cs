using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Json.Schema;
using Microsoft.Extensions.Logging;

namespace Axis.PackageHost.Tests;

/// <summary>
/// Host mode over real loopback TCP, with real runner processes that run the test package 1.0.0 and the Python test
/// package from the test output, see docs/reference/extension-packages.md#the-runner-protocol. The Python package is
/// copied with its dependency folder, so the tests need no pip, and they run only where <c>python3</c> is on the path. A stub on a loopback port answers the
/// package's HTTP request. Every line the tests send and receive is checked against the protocol's schema.
/// Each wait ends on a state, under a cancellation deadline that only bounds a hang.
/// </summary>
public sealed class PackageHostTests : IAsyncLifetime
{
    private const string PythonId = "python-test-package";
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";

    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(90);

    private static readonly JsonSchema _schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "package-runner-protocol.schema.json"));

    private readonly TemporaryFolder _extensions = new();
    private readonly CapturingLogger _log = new();
    private readonly List<TcpClient> _clients = [];
    private HttpListener _stub = null!;
    private RunnerHost _host = null!;
    private IPEndPoint _endpoint = null!;
    private Task _stubLoop = Task.CompletedTask;
    private Uri _stubAddress = null!;
    private string? _authorization;
    private string? _idempotencyKey;
    private string? _requestTarget;

    public async ValueTask InitializeAsync()
    {
        _extensions.Install("1.0.0");
        InstallPython();
        var port = FreePort();
        _stubAddress = new Uri($"http://127.0.0.1:{port}/");
        _stub = new HttpListener();
        _stub.Prefixes.Add(_stubAddress.AbsoluteUri);
        _stub.Start();
        _stubLoop = AnswerStubAsync();

        _host = new RunnerHost(_extensions.Path, TestRunnerCommand.Create(), PythonRunnerCommand.ForThisProgram(), _log);
        _endpoint = await _host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await _host.DisposeAsync();
        _stub.Close();
        await _stubLoop;
        _extensions.Dispose();
    }

    [Fact]
    public async Task A_declared_operation_runs_and_returns_typed_values()
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        Assert.Equal(new ReadyMessage(), await connection.ReceiveAsync());

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = "declared" }, withBindings: true);

        var result = Assert.IsType<ResultMessage>(probe.Answer);
        Assert.Equal("200", result.Output["status"]!.GetValue<string>());
        Assert.Equal("Bearer secret-token", _authorization);
        Assert.Equal(probe.OperationId.ToString("D"), _idempotencyKey);
        Assert.StartsWith(_stubAddress.GetLeftPart(UriPartial.Authority), _requestTarget, StringComparison.Ordinal);

        var sum = await connection.RunAsync("sum", new JsonObject { ["a"] = "9007199254740993", ["b"] = "1" }, withBindings: false);
        Assert.Equal("9007199254740994", Assert.IsType<ResultMessage>(sum.Answer).Output["total"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("undeclaredSecret")]
    [InlineData("swallow")]
    public async Task An_undeclared_secret_fails_the_run_with_its_binding_even_when_the_operation_catches_it(string mode)
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = mode }, withBindings: true);

        var failure = Assert.IsType<FailureMessage>(probe.Answer);
        Assert.Equal("OtherToken", failure.Binding);
        Assert.Null(failure.Error);
        Assert.DoesNotContain("The operation", probe.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_to_a_host_that_no_url_binding_declares_fails_the_run_with_the_url_binding()
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = "otherHost" }, withBindings: true);

        Assert.Equal(new FailureMessage(null, "TestUrl"), probe.Answer);
    }

    [Fact]
    public async Task A_retryable_exception_gives_retryable_with_the_type_name_and_no_message()
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();

        var flaky = await connection.RunAsync("flaky", new JsonObject { ["failures"] = "1" }, withBindings: false);

        Assert.Equal(new RetryableMessage("Axis.Extensions.RetryableException"), flaky.Answer);
        Assert.DoesNotContain("Attempt", flaky.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_exception_gives_failure_with_the_type_name_and_no_message()
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();

        var fail = await connection.RunAsync("fail", new JsonObject { ["a"] = "4242424242" }, withBindings: false);

        Assert.Equal(new FailureMessage("System.FormatException", null), fail.Answer);
        Assert.DoesNotContain("4242424242", fail.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "System.ArgumentException")]
    [InlineData("sum", "System.FormatException")]
    public async Task An_unknown_operation_or_an_unreadable_input_gives_failure_with_the_type_name(string operation, string error)
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();

        var run = await connection.RunAsync(operation, new JsonObject { ["a"] = "not a number", ["b"] = "1" }, withBindings: false);

        Assert.Equal(new FailureMessage(error, null), run.Answer);
    }

    [Fact]
    public async Task A_python_operation_runs_and_returns_typed_values_even_when_the_adapter_prints_at_import()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(PythonId, "1.0.0");

        // The adapter prints at import, and the first line is still the ready.
        Assert.Equal(new ReadyMessage(), await connection.ReceiveAsync());

        var echoed = new JsonObject
        {
            ["text"] = "Zoë ✓",
            ["integer"] = "-9007199254740993",
            ["decimal"] = "12345678901234567890.123456789",
            ["boolean"] = true,
            ["date"] = "2024-02-29",
            ["dateTime"] = "2024-02-29T13:45:10.123456+07:00",
        };
        var echo = await connection.RunAsync("echo", JsonNode.Parse(echoed.ToJsonString())!.AsObject(), withBindings: false);
        Assert.True(JsonNode.DeepEquals(echoed, Assert.IsType<ResultMessage>(echo.Answer).Output), echo.Line);

        var greet = await connection.RunAsync("greet", new JsonObject { ["name"] = "Axis" }, withBindings: false);
        Assert.Equal("Hello, Axis", Assert.IsType<ResultMessage>(greet.Answer).Output["greeting"]!.GetValue<string>());

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = "declared" }, withBindings: true);
        Assert.Equal("200", Assert.IsType<ResultMessage>(probe.Answer).Output["status"]!.GetValue<string>());
        Assert.Equal("Bearer secret-token", _authorization);
        Assert.Equal(probe.OperationId.ToString("D"), _idempotencyKey);
        Assert.StartsWith(_stubAddress.GetLeftPart(UriPartial.Authority), _requestTarget, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_python_retryable_error_gives_retryable_with_the_type_name_and_no_message()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(PythonId, "1.0.0");
        await connection.ReceiveAsync();

        var flaky = await connection.RunAsync("flaky", new JsonObject { ["a"] = "x" }, withBindings: false);

        Assert.Equal(new RetryableMessage("axis_sdk.RetryableError"), flaky.Answer);
        Assert.DoesNotContain("Attempt", flaky.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_python_exception_gives_failure_with_the_type_name_and_no_message()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(PythonId, "1.0.0");
        await connection.ReceiveAsync();

        var fail = await connection.RunAsync("fail", new JsonObject { ["a"] = "4242424242" }, withBindings: false);

        Assert.Equal(new FailureMessage("ValueError", null), fail.Answer);
        Assert.DoesNotContain("4242424242", fail.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "LookupError")]
    [InlineData("echo", "ValueError")]
    public async Task A_python_unknown_operation_or_an_unreadable_input_gives_failure_with_the_type_name(string operation, string error)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(PythonId, "1.0.0");
        await connection.ReceiveAsync();

        var run = await connection.RunAsync(operation, new JsonObject { ["integer"] = "not a number" }, withBindings: false);

        Assert.Equal(new FailureMessage(error, null), run.Answer);
    }

    [Theory]
    [InlineData("undeclaredSecret")]
    [InlineData("swallow")]
    public async Task A_python_undeclared_secret_fails_the_run_with_its_binding_even_when_the_operation_catches_it(string mode)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(PythonId, "1.0.0");
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = mode }, withBindings: true);

        var failure = Assert.IsType<FailureMessage>(probe.Answer);
        Assert.Equal("OtherToken", failure.Binding);
        Assert.Null(failure.Error);
    }

    [Fact]
    public async Task A_python_request_to_a_host_that_no_url_binding_declares_fails_the_run_with_the_url_binding()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(PythonId, "1.0.0");
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = "otherHost" }, withBindings: true);

        Assert.Equal(new FailureMessage(null, "TestUrl"), probe.Answer);
    }

    [Fact]
    public async Task Closing_the_connection_ends_the_runner_process()
    {
        var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();
        var runner = Assert.Single(_log.StartedRunners);
        using var process = Process.GetProcessById(runner);
        Assert.False(process.HasExited);

        connection.Dispose();

        using var deadline = new CancellationTokenSource(_deadline);
        await process.WaitForExitAsync(deadline.Token);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task A_runner_that_exits_closes_its_connection()
    {
        using var connection = await StartAsync(TestPackages.Id, "1.0.0");
        await connection.ReceiveAsync();
        var runner = Assert.Single(_log.StartedRunners);
        using var process = Process.GetProcessById(runner);

        // A line that is not a message ends the runner.
        await connection.SendRawAsync("not json");

        using var deadline = new CancellationTokenSource(_deadline);
        await process.WaitForExitAsync(deadline.Token);
        Assert.Null(await connection.ReceiveAsync());
    }

    [Fact]
    public async Task A_start_for_a_version_that_is_not_installed_closes_the_connection_and_starts_no_runner()
    {
        using var connection = await StartAsync(TestPackages.Id, "9.9.9");

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Theory]
    [InlineData("..", "1.0.0")]
    [InlineData("test-package", "..")]
    [InlineData("test-package/..", "1.0.0")]
    [InlineData("TEST-PACKAGE", "1.0.0")]
    [InlineData("test-package", "1.0.0\n")]
    public async Task A_start_with_a_malformed_id_or_version_closes_the_connection_and_starts_no_runner(string package, string version)
    {
        using var connection = await StartAsync(package, version, wellFormed: false);

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Theory]
    [InlineData("{\"type\":\"ready\"}")]
    [InlineData("{\"type\":\"start\",\"package\":\"test-package\",\"version\":\"1.0.0\"}")]
    [InlineData("{\"type\":\"start\",\"package\":\"test-package\",\"version\":\"1.0.0\",\"proxy\":\"nohost\",\"proxyToken\":\"t\"}")]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("not json")]
    public async Task A_first_line_that_is_not_a_start_closes_the_connection_and_starts_no_runner(string line)
    {
        using var connection = Connect();
        await connection.SendRawAsync(line);

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Fact]
    public async Task A_connection_that_closes_before_it_sends_a_start_starts_no_runner()
    {
        Connect().Dispose();

        // A second connection that starts a runner proves that the host went on after the first.
        using var next = await StartAsync(TestPackages.Id, "1.0.0");
        Assert.IsType<ReadyMessage>(await next.ReceiveAsync());
        Assert.Single(_log.StartedRunners);
    }

    /// <summary>Copies the Python test package and its dependency folder into one version folder, as a build leaves it, without pip.</summary>
    private void InstallPython()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "PythonPackage");
        var target = Path.Combine(_extensions.Path, PythonId, "1.0.0");
        foreach (var folder in new[] { "package", "dependency" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(source, folder), "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(Path.Combine(source, folder), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
    }

    /// <summary>
    /// Connects and sends a <c>start</c> that names the stub as the egress proxy, which answers each request the runner
    /// sends through its environment proxy.
    /// A start that the test means to be malformed is not checked against the schema.
    /// </summary>
    private async Task<Connection> StartAsync(string package, string version, bool wellFormed = true)
    {
        var connection = Connect();
        await connection.SendAsync(new StartMessage(package, version, $"127.0.0.1:{_stubAddress.Port}", "proxy-token"), wellFormed);
        return connection;
    }

    private Connection Connect()
    {
        var client = new TcpClient();
        client.Connect(_endpoint);
        _clients.Add(client);
        return new Connection(client, _stubAddress);
    }

    /// <summary>Answers each request of the package with 200 and keeps the headers the tests check.</summary>
    private async Task AnswerStubAsync()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _stub.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _authorization = context.Request.Headers["Authorization"];
            _idempotencyKey = context.Request.Headers["Idempotency-Key"];
            _requestTarget = context.Request.RawUrl;
            context.Response.StatusCode = 200;
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void AssertValid(string line)
    {
        using var document = JsonDocument.Parse(line);
        var results = _schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(results.IsValid, $"The line does not match the schema: {line}");
    }

    /// <summary>The connection of a test, whose lines are checked against the schema.</summary>
    private sealed class Connection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly Uri _stubAddress;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        public Connection(TcpClient client, Uri stubAddress)
        {
            _client = client;
            _stubAddress = stubAddress;
            var stream = client.GetStream();
            _reader = new StreamReader(stream, new UTF8Encoding(false));
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        public async Task SendAsync(RunnerMessage message, bool wellFormed = true)
        {
            var line = RunnerProtocol.Write(message);
            if (wellFormed)
            {
                AssertValid(line);
            }

            await _writer.WriteLineAsync(line);
        }

        public Task SendRawAsync(string line) => _writer.WriteLineAsync(line);

        /// <summary>The next message, or <see langword="null"/> when the host closed the connection.</summary>
        public async Task<RunnerMessage?> ReceiveAsync() => (await ReceiveLineAsync()) is { } line ? RunnerProtocol.Read(line) : null;

        public async Task<(RunnerMessage Answer, string Line, Guid OperationId)> RunAsync(string operation, JsonObject input, bool withBindings)
        {
            var operationId = Guid.NewGuid();
            var bindings = withBindings
                ? new RunBindings(new() { ["TestUrl"] = _stubAddress.AbsoluteUri }, new() { ["TestToken"] = "secret-token" })
                : new RunBindings([], []);
            await SendAsync(new RunMessage(operation, operationId, input, bindings));
            var line = await ReceiveLineAsync() ?? throw new InvalidOperationException("The connection closed before the answer.");
            return (RunnerProtocol.Read(line), line, operationId);
        }

        public void Dispose() => _client.Dispose();

        private async Task<string?> ReceiveLineAsync()
        {
            using var deadline = new CancellationTokenSource(_deadline);
            try
            {
                var line = await _reader.ReadLineAsync(deadline.Token);
                if (line is not null)
                {
                    AssertValid(line);
                }

                return line;
            }
            catch (IOException)
            {
                // The host closed the connection with unread data from us, so the peer reset it.
                return null;
            }
        }
    }

    /// <summary>Keeps the process ids of the runners that the host logged as started.</summary>
    private sealed class CapturingLogger : ILogger<RunnerHost>
    {
        private readonly List<int> _started = [];

        public IReadOnlyList<int> StartedRunners
        {
            get
            {
                lock (_started)
                {
                    return [.. _started];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values
                && values.FirstOrDefault(pair => pair.Key == "ProcessId").Value is int processId)
            {
                lock (_started)
                {
                    _started.Add(processId);
                }
            }
        }
    }
}
