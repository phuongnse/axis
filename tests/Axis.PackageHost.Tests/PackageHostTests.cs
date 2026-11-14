using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Json.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PackageTestMessage = Axis.Configuration.Packages.TestMessage;

namespace Axis.PackageHost.Tests;

/// <summary>
/// Host mode over real loopback TCP, with real runner processes that run the test package 1.0.0 and the Python test
/// package from the test output, see docs/reference/extension-packages.md#the-runner-protocol. The Python package is
/// copied with its dependency folder, so the tests need no pip, and they run only where <c>python3</c> is on the path. A stub on a loopback port answers the
/// package's HTTP request. Every line the tests send and receive is checked against the protocol's schema.
/// Each wait ends on a state, under a cancellation deadline that only bounds a hang. The Node.js tests run the
/// probe package, a plain ES module, and the built Node.js test package, which is CommonJS, through the Node.js runner
/// that the test project copies to the test output. One test builds a package, and every build writes and deletes
/// folders named axis-package-build-* in the temporary folder, so this class runs one after the other with the classes
/// that build packages. Some tests start from files, and each of those writes and deletes a folder named axis-runner-* in the
/// temporary folder, so the same rule holds for them.
/// </summary>
[Collection(BuildTest.Collection)]
public sealed class PackageHostTests : IAsyncLifetime
{
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";
    private const string NodeProbe = "node-probe-package";
    private const string NoFindings = """{ "Results": [] }""";

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
    private TcpListener _tunnels = null!;
    private Task _tunnelLoop = Task.CompletedTask;
    private string? _connectTarget;
    private string? _proxyAuthorization;

    public async ValueTask InitializeAsync()
    {
        _extensions.Install("1.0.0");
        _extensions.InstallPython();
        if (!OperatingSystem.IsWindows())
        {
            BuildTest.CopyFolder(Path.Combine(AppContext.BaseDirectory, "NodeProbePackage"), Path.Combine(_extensions.Path, NodeProbe, "1.0.0"));
        }

        var port = FreePort();
        _stubAddress = new Uri($"http://127.0.0.1:{port}/");
        _stub = new HttpListener();
        _stub.Prefixes.Add(_stubAddress.AbsoluteUri);
        _stub.Start();
        _stubLoop = AnswerStubAsync();
        _tunnels = new TcpListener(IPAddress.Loopback, 0);
        _tunnels.Start();
        _tunnelLoop = AcceptTunnelsAsync();

        _host = new RunnerHost(_extensions.Path, TestRunnerCommand.Create(), _log);
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
        _tunnels.Stop();
        await _tunnelLoop;
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
        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0");

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
        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0");
        await connection.ReceiveAsync();

        var flaky = await connection.RunAsync("flaky", new JsonObject { ["a"] = "x" }, withBindings: false);

        Assert.Equal(new RetryableMessage("axis_sdk.RetryableError"), flaky.Answer);
        Assert.DoesNotContain("Attempt", flaky.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_python_exception_gives_failure_with_the_type_name_and_no_message()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0");
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
        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0");
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
        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0");
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
        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0");
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

    [Theory]
    [InlineData("<6>[    0.000000] Starting gVisor...\n<6>[    0.100000] Waiting for children...\n", true)]
    [InlineData("Starting gVisor...\n", true)]
    [InlineData("<5>[    0.000000] Linux version 6.8.0 (build@host)\n<6>[    0.000001] Starting gVisor...\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task A_sandbox_question_is_answered_from_the_kernel_log_and_starts_no_runner(string? kernelLog, bool gvisor)
    {
        await using var host = new RunnerHost(_extensions.Path, TestRunnerCommand.Create(), _log, new Sandbox(() => kernelLog));
        var endpoint = await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);
        using var connection = Connect(endpoint);
        await connection.SendAsync(new SandboxMessage());

        Assert.Equal(new SandboxMessage(gvisor), await connection.ReceiveAsync());
        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Fact]
    public async Task A_sandbox_line_that_already_holds_an_answer_closes_the_connection_without_one()
    {
        await using var host = new RunnerHost(_extensions.Path, TestRunnerCommand.Create(), _log, new Sandbox(() => "Starting gVisor"));
        var endpoint = await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);
        using var connection = Connect(endpoint);
        await connection.SendAsync(new SandboxMessage(true));

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Fact]
    public void This_machine_is_not_under_gvisor_because_the_tests_never_run_under_it() =>
        Assert.False(Sandbox.ForThisMachine().IsGvisor());

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

    [Fact]
    public async Task A_start_with_a_python_package_s_files_runs_it_from_a_temporary_copy_and_not_from_the_extensions_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var empty = new TemporaryFolder();
        await using var host = new RunnerHost(empty.Path, TestRunnerCommand.Create(), _log);
        var endpoint = await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);

        using var connection = await StartFromFilesAsync(TestPackages.PythonId, "1.0.0", PythonFiles(), endpoint: endpoint);

        Assert.Equal(new ReadyMessage(), await connection.ReceiveAsync());
        var greet = await connection.RunAsync("greet", new JsonObject { ["name"] = "Axis" }, withBindings: false);
        Assert.Equal("Hello, Axis", Assert.IsType<ResultMessage>(greet.Answer).Output["greeting"]!.GetValue<string>());
        Assert.Single(_log.StartedRunners);
    }

    [Fact]
    public async Task Closing_a_connection_that_started_from_files_deletes_the_runner_s_temporary_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        var before = RunnerFolders();
        var connection = await StartFromFilesAsync(TestPackages.PythonId, "1.0.0", PythonFiles());
        Assert.Equal(new ReadyMessage(), await connection.ReceiveAsync());

        var folder = Assert.Single(RunnerFolders().Except(before));
        Assert.True(File.Exists(Path.Combine(folder, TestPackages.PythonId, "1.0.0", "axis-package.json")));

        connection.Dispose();
        var deadline = DateTime.UtcNow + _deadline;
        while (Directory.Exists(folder) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData("path", "1.0.0", true, true)]
    [InlineData("version", "1.0.1", true, true)]
    [InlineData("tenant", "1.0.0", false, false)]
    public async Task A_start_from_files_with_a_bad_path_another_version_or_no_tenant_starts_no_runner(string defect, string version, bool withTenant, bool wellFormed)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        var before = RunnerFolders();
        var files = PythonFiles();
        if (defect == "path")
        {
            files["../x"] = "x"u8.ToArray();
        }

        using var connection = await StartFromFilesAsync(TestPackages.PythonId, version, files, withTenant ? "tenant-a" : null, wellFormed);

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
        Assert.Equal(before.Order(), RunnerFolders().Order());
    }

    [Fact]
    public async Task A_node_package_runs_and_returns_typed_values()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        Assert.Equal(new ReadyMessage(), await connection.ReceiveAsync());

        var sum = await connection.RunAsync("sum", new JsonObject { ["a"] = "9007199254740993", ["b"] = "1" }, withBindings: false);

        Assert.Equal("9007199254740994", Assert.IsType<ResultMessage>(sum.Answer).Output["total"]!.GetValue<string>());
        Assert.Single(_log.StartedRunners);
    }

    [Fact]
    public async Task A_node_package_operation_name_is_matched_ignoring_letter_case()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var sum = await connection.RunAsync("SUM", new JsonObject { ["a"] = "2", ["b"] = "3" }, withBindings: false);

        Assert.Equal("5", Assert.IsType<ResultMessage>(sum.Answer).Output["total"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_node_package_reaches_the_declared_host_through_the_proxy_with_its_secret_and_operation_id()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0", proxyPort: ((IPEndPoint)_tunnels.LocalEndpoint).Port);
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = "declared" }, withBindings: true);

        Assert.Equal("200", Assert.IsType<ResultMessage>(probe.Answer).Output["status"]!.GetValue<string>());
        Assert.Equal("Bearer secret-token", _authorization);
        Assert.Equal(probe.OperationId.ToString("D"), _idempotencyKey);
        // Node.js tunnels each request through the proxy, so the proxy saw a CONNECT to the declared host, with the runner's token.
        Assert.Equal($"127.0.0.1:{_stubAddress.Port}", _connectTarget);
        Assert.Equal("Basic " + Convert.ToBase64String("axis:proxy-token"u8), _proxyAuthorization);
        Assert.Equal("/probe", _requestTarget);
    }

    [Fact]
    public async Task A_node_package_request_to_a_host_that_no_url_binding_declares_fails_the_run_with_the_url_binding()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = "otherHost" }, withBindings: true);

        Assert.Equal(new FailureMessage(null, "TestUrl"), probe.Answer);
    }

    [Theory]
    [InlineData("undeclaredSecret")]
    [InlineData("swallow")]
    public async Task A_node_package_asking_for_an_undeclared_secret_fails_the_run_with_its_binding_even_when_it_catches_the_error(string mode)
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var probe = await connection.RunAsync("probe", new JsonObject { ["mode"] = mode }, withBindings: true);

        var failure = Assert.IsType<FailureMessage>(probe.Answer);
        Assert.Equal("OtherToken", failure.Binding);
        Assert.Null(failure.Error);
        Assert.DoesNotContain("The operation", probe.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_node_package_error_named_RetryableError_gives_retryable_with_its_name_and_no_message()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var flaky = await connection.RunAsync("flaky", new JsonObject { ["failures"] = "1" }, withBindings: false);

        Assert.Equal(new RetryableMessage("RetryableError"), flaky.Answer);
        Assert.DoesNotContain("Attempt", flaky.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_node_package_error_gives_failure_with_its_name_and_no_message()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var fail = await connection.RunAsync("fail", new JsonObject { ["a"] = "4242424242" }, withBindings: false);

        Assert.Equal(new FailureMessage("RangeError", null), fail.Answer);
        Assert.DoesNotContain("4242424242", fail.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "not a number")]
    [InlineData("sum", "not a number")]
    [InlineData("sum", "9223372036854775808")]
    public async Task A_node_package_unknown_operation_or_input_that_does_not_fit_gives_failure_with_TypeError_and_no_value(string operation, string a)
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var run = await connection.RunAsync(operation, new JsonObject { ["a"] = a, ["b"] = "1" }, withBindings: false);

        Assert.Equal(new FailureMessage("TypeError", null), run.Answer);
        Assert.DoesNotContain(a, run.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_node_package_integer_output_that_is_a_JavaScript_number_gives_failure_with_an_error_and_no_output()
    {
        SkipOnWindows();
        using var connection = await StartAsync(NodeProbe, "1.0.0");
        await connection.ReceiveAsync();

        var run = await connection.RunAsync("badOutput", [], withBindings: false);

        Assert.Equal(new FailureMessage("TypeError", null), run.Answer);
        Assert.DoesNotContain("output", run.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_node_runner_sends_console_output_of_the_package_to_standard_error_and_keeps_the_protocol_stream_intact()
    {
        SkipOnWindows();
        var info = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "sdk", "node", "runner.js"));
        info.ArgumentList.Add("runner");
        info.ArgumentList.Add(_extensions.Path);
        info.ArgumentList.Add(NodeProbe);
        info.ArgumentList.Add("1.0.0");
        using var runner = Process.Start(info)!;
        var run = new RunMessage("log", Guid.NewGuid(), [], new RunBindings([], []));
        var sent = new[] { RunnerProtocol.Write(run), RunnerProtocol.Write(new StopMessage()) };
        foreach (var line in sent)
        {
            AssertValid(line);
            await runner.StandardInput.WriteLineAsync(line);
        }

        runner.StandardInput.Close();

        using var deadline = new CancellationTokenSource(_deadline);
        var output = runner.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = runner.StandardError.ReadToEndAsync(deadline.Token);
        await runner.WaitForExitAsync(deadline.Token);

        var lines = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            AssertValid(line);
        }

        Assert.Equal(new ReadyMessage(), RunnerProtocol.Read(lines[0]));
        Assert.True(Assert.IsType<ResultMessage>(RunnerProtocol.Read(lines[1])).Output["done"]!.GetValue<bool>());
        Assert.DoesNotContain("axis-console-marker-4242424242", lines[1], StringComparison.Ordinal);
        Assert.Contains("axis-console-marker-4242424242", await error, StringComparison.Ordinal);
        Assert.Equal(0, runner.ExitCode);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_built_TypeScript_node_test_package_answers_its_operation_through_the_runner()
    {
        SkipOnWindows();
        using var test = new BuildTest("""{ "Results": [] }""", node: true);
        var (exitCode, _) = await test.RunAsync();
        Assert.Equal(PackageBuildCommand.Built, exitCode);
        await using var host = new RunnerHost(test.Extensions.Path, TestRunnerCommand.Create(), _log);
        var endpoint = await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);

        using var connection = await StartAsync(BuildTest.NodeId, "1.0.0", endpoint: endpoint);
        Assert.Equal(new ReadyMessage(), await connection.ReceiveAsync());
        var greet = await connection.RunAsync("greet", new JsonObject { ["name"] = "Axis" }, withBindings: false);

        Assert.Contains("Axis", Assert.IsType<ResultMessage>(greet.Answer).Output["greeting"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_for_a_python_version_whose_runtime_is_missing_closes_the_connection_and_starts_no_runner()
    {
        var commands = TestRunnerCommand.Create() with { Python = new RunnerCommand("axis-no-such-python-runtime", []) };
        await using var host = new RunnerHost(_extensions.Path, commands, _log);
        var endpoint = await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);

        using var connection = await StartAsync(TestPackages.PythonId, "1.0.0", endpoint: endpoint);

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Fact]
    public async Task A_start_for_a_node_version_whose_runtime_is_missing_closes_the_connection_and_starts_no_runner()
    {
        var commands = TestRunnerCommand.Create() with { Node = new RunnerCommand("axis-no-such-node-runtime", []) };
        await using var host = new RunnerHost(_extensions.Path, commands, _log);
        var endpoint = await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None);

        using var connection = await StartAsync(NodeProbe, "1.0.0", endpoint: endpoint);

        Assert.Null(await connection.ReceiveAsync());
        Assert.Empty(_log.StartedRunners);
    }

    [Theory]
    [InlineData("../evil.py")]
    [InlineData("/abs.py")]
    [UnsupportedOSPlatform("windows")]
    public async Task A_build_with_a_path_outside_the_package_is_refused_naming_the_path_and_writes_no_file(string path)
    {
        SkipOnWindows();
        using var scripts = new TemporaryFolder();
        await using var host = await StartBuildHostAsync(scripts);
        var outside = Path.GetFullPath(path, Path.Combine(Path.GetTempPath(), "axis-copy"));
        using var connection = Connect(host.Endpoint);

        await connection.SendAsync(new BuildMessage(new()
        {
            ["axis-package.json"] = File.ReadAllBytes(Path.Combine(InitializerFolder, "axis-package.json")),
            [path] = "print('evil')"u8.ToArray(),
        }));

        var refused = Assert.IsType<RefusedMessage>(await connection.ReceiveAsync());
        var problem = Assert.Single(refused.Problems);
        Assert.Equal(path, problem.File);
        Assert.Contains(path, problem.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(outside));
        Assert.Null(await connection.ReceiveAsync());
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_build_of_a_C_sharp_package_answers_its_files_and_runs_none_of_its_code_in_the_host()
    {
        SkipOnWindows();
        using var scripts = new TemporaryFolder();
        await using var host = await StartBuildHostAsync(scripts);
        var files = Directory.GetFiles(InitializerFolder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(InitializerFolder, file).Replace('\\', '/'), File.ReadAllBytes);
        using var connection = Connect(host.Endpoint);

        await connection.SendAsync(new BuildMessage(files));

        var built = Assert.IsType<BuiltMessage>(await connection.ReceiveAsync());
        Assert.Contains("axis-package.json", built.Files.Keys);
        Assert.DoesNotContain(built.Files.Keys, name => name.EndsWith(".marker", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(_extensions.Path, "initializer-package")));
        Assert.Null(await connection.ReceiveAsync());
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_test_of_a_Python_package_answers_the_outcome_of_each_test_and_logs_it()
    {
        SkipOnWindows();
        using var package = BuildTest.Python(NoFindings);
        await using var host = await StartBuildHostAsync(package.Scripts);
        var files = SourceFiles(package, new()
        {
            ["test_sample.py"] = Encoding.UTF8.GetBytes("""
                import unittest

                from adapter import greet


                class SampleTests(unittest.TestCase):
                    def test_greets(self):
                        self.assertEqual(greet({'name': 'Axis'}, None), {'greeting': 'Hello, Axis'})

                    def test_fails(self):
                        self.assertEqual(1, 2)
                """),
        });
        using var connection = Connect(host.Endpoint);

        await connection.SendAsync(new PackageTestMessage(files));

        var tested = Assert.IsType<TestedMessage>(await connection.ReceiveAsync());
        Assert.Equal(2, tested.Tests.Count);
        var passed = Assert.Single(tested.Tests, test => test.Name == "test_sample.SampleTests.test_greets");
        Assert.Equal(PackageTestOutcome.Passed, passed.Outcome);
        Assert.Null(passed.Message);
        var failed = Assert.Single(tested.Tests, test => test.Name == "test_sample.SampleTests.test_fails");
        Assert.Equal(PackageTestOutcome.Failed, failed.Outcome);
        Assert.StartsWith("AssertionError", failed.Message, StringComparison.Ordinal);
        Assert.Contains("Tested package python-test-package 1.0.0.", _log.Messages);
        Assert.Null(await connection.ReceiveAsync());
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_test_of_a_Python_package_whose_requirements_do_not_install_is_refused_and_runs_no_test()
    {
        SkipOnWindows();
        using var package = BuildTest.Python(NoFindings, _ => $"./wheels/{BuildTest.WheelName} --hash=sha256:{new string('0', 64)}\n");
        using var markers = new TemporaryFolder();
        var marker = Path.Combine(markers.Path, "imported");
        await using var host = await StartBuildHostAsync(package.Scripts);
        var files = SourceFiles(package, new()
        {
            ["test_marker.py"] = Encoding.UTF8.GetBytes($"open({JsonSerializer.Serialize(marker)}, 'w').close()\n"),
        });
        using var connection = Connect(host.Endpoint);

        await connection.SendAsync(new PackageTestMessage(files));

        var refused = Assert.IsType<RefusedMessage>(await connection.ReceiveAsync());
        Assert.Equal("requirements.txt", Assert.Single(refused.Problems).File);
        Assert.False(File.Exists(marker));
        Assert.DoesNotContain(_log.Messages, message => message.StartsWith("Tested package", StringComparison.Ordinal));
        Assert.Null(await connection.ReceiveAsync());
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_test_that_runs_past_the_limit_is_stopped_and_refused_at_the_manifest_and_its_copy_is_deleted()
    {
        SkipOnWindows();
        using var package = BuildTest.Python(NoFindings);
        using var markers = new TemporaryFolder();
        var marker = Path.Combine(markers.Path, "folder");
        await using var host = await StartBuildHostAsync(package.Scripts, testLimit: TimeSpan.FromSeconds(3));
        var files = SourceFiles(package, new()
        {
            ["test_sleep.py"] = Encoding.UTF8.GetBytes($"""
                import os
                import time
                import unittest


                class SleepTests(unittest.TestCase):
                    def test_sleeps(self):
                        with open({JsonSerializer.Serialize(marker)}, 'w') as file:
                            file.write(os.path.dirname(os.path.abspath(__file__)))
                        time.sleep(600)
                """),
        });
        using var connection = Connect(host.Endpoint);

        await connection.SendAsync(new PackageTestMessage(files));

        var refused = Assert.IsType<RefusedMessage>(await connection.ReceiveAsync());
        Assert.Equal(PackageCatalog.ManifestFileName, Assert.Single(refused.Problems).File);
        Assert.Null(await connection.ReceiveAsync());
        Assert.False(Directory.Exists(await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken)));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_test_of_a_Node_package_is_refused_with_one_problem_at_the_manifest()
    {
        SkipOnWindows();
        using var scripts = new TemporaryFolder();
        await using var host = await StartBuildHostAsync(scripts);
        var folder = Path.Combine(AppContext.BaseDirectory, "NodeProbePackage");
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllBytes);
        using var connection = Connect(host.Endpoint);

        await connection.SendAsync(new PackageTestMessage(files));

        var refused = Assert.IsType<RefusedMessage>(await connection.ReceiveAsync());
        Assert.Equal(PackageCatalog.ManifestFileName, Assert.Single(refused.Problems).File);
        Assert.Null(await connection.ReceiveAsync());
    }

    /// <summary>Every file of the Python test package of <paramref name="package"/>, with its path that uses <c>/</c>, and the <paramref name="extra"/> files.</summary>
    [UnsupportedOSPlatform("windows")]
    private static Dictionary<string, byte[]> SourceFiles(BuildTest package, Dictionary<string, byte[]> extra)
    {
        var files = Directory.GetFiles(package.Source, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(package.Source, file).Replace('\\', '/'), File.ReadAllBytes);
        foreach (var (path, content) in extra)
        {
            files[path] = content;
        }

        return files;
    }

    private static string InitializerFolder => Path.Combine(AppContext.BaseDirectory, "TestPackages", TestPackages.InitializerBuild, "initializer-package", "1.0.0");

    /// <summary>A host whose Trivy is a stub that reports no vulnerabilities.</summary>
    [UnsupportedOSPlatform("windows")]
    private async Task<BuildHost> StartBuildHostAsync(TemporaryFolder scripts, TimeSpan? testLimit = null)
    {
        var trivy = Path.Combine(scripts.Path, "trivy");
        File.WriteAllText(trivy, "#!/bin/sh\necho '{}'\n");
        File.SetUnixFileMode(trivy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy }).Build();
        var host = new RunnerHost(_extensions.Path, TestRunnerCommand.Create(), _log, configuration: configuration, testLimit: testLimit);
        return new BuildHost(host, await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None));
    }

    private sealed class BuildHost(RunnerHost host, IPEndPoint endpoint) : IAsyncDisposable
    {
        public IPEndPoint Endpoint { get; } = endpoint;

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }

    /// <summary>
    /// Connects and sends a <c>start</c> that names the stub as the egress proxy, which answers each request the runner
    /// sends through its environment proxy.
    /// A start that the test means to be malformed is not checked against the schema.
    /// </summary>
    private async Task<Connection> StartAsync(string package, string version, bool wellFormed = true, IPEndPoint? endpoint = null, int? proxyPort = null)
    {
        var connection = Connect(endpoint);
        await connection.SendAsync(new StartMessage(package, version, $"127.0.0.1:{proxyPort ?? _stubAddress.Port}", "proxy-token"), wellFormed);
        return connection;
    }

    /// <summary>Connects and sends a <c>start</c> that carries <paramref name="files"/> and the tenant. A start that the test means to be malformed is not checked against the schema.</summary>
    private async Task<Connection> StartFromFilesAsync(
        string package, string version, Dictionary<string, byte[]> files, string? tenant = "tenant-a", bool wellFormed = true, IPEndPoint? endpoint = null)
    {
        var connection = Connect(endpoint);
        await connection.SendAsync(new StartMessage(package, version, $"127.0.0.1:{_stubAddress.Port}", "proxy-token", tenant, files), wellFormed);
        return connection;
    }

    /// <summary>The built files of the installed Python test package, each under its relative path that uses <c>/</c>.</summary>
    private Dictionary<string, byte[]> PythonFiles()
    {
        var folder = Path.Combine(_extensions.Path, TestPackages.PythonId, "1.0.0");
        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllBytes);
    }

    private static string[] RunnerFolders() => Directory.GetDirectories(Path.GetTempPath(), "axis-runner-*");

    private Connection Connect(IPEndPoint? endpoint = null)
    {
        var client = new TcpClient();
        client.Connect(endpoint ?? _endpoint);
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

    /// <summary>Accepts the connections of the Node.js runner's tunnels, see <see cref="TunnelAsync"/>.</summary>
    private async Task AcceptTunnelsAsync()
    {
        var tunnels = new List<Task>();
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _tunnels.AcceptTcpClientAsync();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or SocketException)
            {
                break;
            }

            tunnels.Add(TunnelAsync(client));
        }

        await Task.WhenAll(tunnels);
    }

    /// <summary>
    /// Acts as the egress proxy of a Node.js runner, whose <c>fetch</c> asks the proxy for a <c>CONNECT</c> tunnel, which the
    /// stub, an <see cref="HttpListener"/>, cannot answer. It keeps the target and the credentials of the <c>CONNECT</c>, then
    /// pipes the tunnel to the target until one side closes.
    /// </summary>
    private async Task TunnelAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var head = new StringBuilder();
            var buffer = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(buffer) == 0)
                {
                    return;
                }

                head.Append((char)buffer[0]);
            }

            var lines = head.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var request = lines[0].Split(' ');
            _connectTarget = request[0] == "CONNECT" ? request[1] : null;
            _proxyAuthorization = lines.Skip(1).Select(line => line.Split(": ", 2)).FirstOrDefault(pair => pair[0].Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))?[1];
            if (_connectTarget is null)
            {
                return;
            }

            using var target = new TcpClient();
            await target.ConnectAsync(IPAddress.Loopback, _stubAddress.Port);
            await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray());
            var upstream = target.GetStream();
            var toTarget = stream.CopyToAsync(upstream);
            var fromTarget = upstream.CopyToAsync(stream);
            await Task.WhenAny(toTarget, fromTarget);

            // Closing both ends settles the copy that is still waiting.
            target.Dispose();
            client.Dispose();
            await Task.WhenAll(toTarget, fromTarget);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
        {
            // The runner or the stub closed the connection.
        }
    }

    private static void SkipOnWindows() => Assert.SkipWhen(OperatingSystem.IsWindows(), "The Node.js tests need a Unix shell and the Node.js runtime.");

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
        private readonly List<string> _messages = [];

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

        /// <summary>Every message that the host logged, as formatted.</summary>
        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_started)
                {
                    return [.. _messages];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_started)
            {
                _messages.Add(formatter(state, exception));
            }

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
