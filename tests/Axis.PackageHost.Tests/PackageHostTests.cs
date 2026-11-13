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
/// Host mode over real loopback TCP, with real runner processes that run the test package 1.0.0 from the test
/// output, see docs/reference/extension-packages.md#the-runner-protocol. A stub on a loopback port answers the
/// package's HTTP request. Every line the tests send and receive is checked against the protocol's schema.
/// Each wait ends on a state, under a cancellation deadline that only bounds a hang.
/// </summary>
public sealed class PackageHostTests : IAsyncLifetime
{
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

    public async ValueTask InitializeAsync()
    {
        _extensions.Install("1.0.0");
        var port = FreePort();
        _stubAddress = new Uri($"http://127.0.0.1:{port}/");
        _stub = new HttpListener();
        _stub.Prefixes.Add(_stubAddress.AbsoluteUri);
        _stub.Start();
        _stubLoop = AnswerStubAsync();

        var assembly = Path.Combine(AppContext.BaseDirectory, "Axis.PackageHost.dll");
        var muxer = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
        var runtimeConfig = Path.Combine(AppContext.BaseDirectory, "Axis.PackageHost.runtimeconfig.json");
        string[] arguments = File.Exists(runtimeConfig) ? ["exec", assembly] : ["exec", "--runtimeconfig", runtimeConfig, assembly];
        _host = new RunnerHost(_extensions.Path, new RunnerCommand(muxer, arguments), _log);
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

    /// <summary>Connects and sends a <c>start</c>. A start that the test means to be malformed is not checked against the schema.</summary>
    private async Task<Connection> StartAsync(string package, string version, bool wellFormed = true)
    {
        var connection = Connect();
        await connection.SendAsync(new StartMessage(package, version, "proxy-token"), wellFormed);
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
