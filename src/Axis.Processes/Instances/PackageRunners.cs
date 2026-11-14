using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;

namespace Axis.Processes.Instances;

/// <summary>
/// The worker's connections to the runners of one package host, the C# one, the Node.js one, the Python one or the application
/// one, see docs/reference/extension-packages.md#package-runners. A runner is one connection, which the host pipes to one
/// runner process, and it runs one call at a time. An idle runner waits in the pool of its package version,
/// and <see cref="RentAsync"/> starts a new one when the pool has none that is still connected. The application pool serves an
/// application's own package, see docs/reference/extension-packages.md#an-applications-packages: its idle runners wait for
/// one tenant, one package version and one build of the package, which is a digest of its built files, so a runner never
/// serves another tenant or a release whose built files differ. A runner that
/// exits, times out or fails is dropped, and closing its connection makes the host kill the process. Runners
/// start when an attempt needs one, so a version never has more of them than calls in flight. The pool uses the
/// worker's <see cref="EgressProxy"/>, which starts with the first runner and which <see cref="PackageRunnerPools"/>
/// owns. Each runner gets its own proxy token in <c>start</c>, together with the address of the proxy as the host
/// reaches it. <paramref name="setting"/> is the name of the setting that holds the host's address.
/// </summary>
internal sealed class PackageRunners(string setting, string? host, int port, EgressProxy proxy) : IAsyncDisposable
{
    /// <summary>
    /// The longest <see cref="RentAsync"/> may take to connect and get <c>ready</c>. It is not part of an operation's
    /// timeout, which starts when the <c>run</c> is sent, so a slow start never counts as an unknown outcome.
    /// </summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string? Tenant, string Package, string Version, string? Build), ConcurrentBag<RunnerConnection>> _idle = new();
    private volatile bool _disposed;

    /// <summary>The name of the setting that holds the address of the package host, for the errors of an attempt.</summary>
    public string Setting => setting;

    /// <summary>Whether <see cref="Setting"/> names a host.</summary>
    public bool IsSet => host is not null;

    /// <summary>
    /// Takes an idle runner of the version whose connection is still open, or connects to the host, sends
    /// <c>start</c> and waits for <c>ready</c>. For an application's package, <paramref name="tenant"/> and the built
    /// <paramref name="files"/> go in <c>start</c>, and an idle runner serves only that tenant and the same digest of files.
    /// </summary>
    /// <exception cref="IOException">The host did not start a runner, so no <c>ready</c> came.</exception>
    /// <exception cref="SocketException">The host cannot be reached.</exception>
    public async Task<RunnerConnection> RentAsync(
        string package,
        string version,
        CancellationToken cancellationToken,
        string? tenant = null,
        IReadOnlyDictionary<string, byte[]>? files = null)
    {
        if (host is null)
        {
            throw new InvalidOperationException("No package host is set.");
        }

        var build = files is null ? null : ReleasePackageFiles.Digest(files);
        if (_idle.TryGetValue((tenant, package, version, build), out var bag))
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
            proxy.EnsureStarted();
            await client.ConnectAsync(host, port, cancellationToken);
            var runner = new RunnerConnection(client, package, version, ProxyAddress(client), proxy.Register(), proxy, tenant, build);
            try
            {
                await runner.StartAsync(files, cancellationToken);
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

    /// <summary>The <c>host:port</c> that a runner started by the host on the other end of <paramref name="client"/> reaches this worker at.</summary>
    private string ProxyAddress(TcpClient client)
    {
        var address = ((IPEndPoint)client.Client.LocalEndPoint!).Address;
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]:{proxy.Port}" : $"{address}:{proxy.Port}";
    }

    /// <summary>Keeps <paramref name="runner"/>, which finished its attempt, for the next one of its version.</summary>
    public void Return(RunnerConnection runner)
    {
        if (_disposed)
        {
            runner.Dispose();
            return;
        }

        _idle.GetOrAdd((runner.Tenant, runner.Package, runner.Version, runner.Build), _ => []).Add(runner);
    }

    /// <summary>Sends <c>stop</c> to each idle runner and closes it. The proxy stays, because the other pool uses it.</summary>
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

/// <summary>
/// The worker's package runner pools, one for each runtime and one for application packages, which share the worker's one
/// <see cref="EgressProxy"/>, see docs/reference/extension-packages.md#package-runners. The C# pool exists unless
/// <c>Extensions:Isolation</c> is <c>inProcess</c>, which loads C# packages into the worker. The Node.js pool always exists,
/// because the worker cannot load JavaScript, and so does the Python pool, because the worker cannot load Python. The
/// <see cref="Application"/> pool always exists too: it runs an application's own package, of any language, in a runner of
/// <c>application-package-host</c>, see docs/reference/extension-packages.md#an-applications-packages.
/// </summary>
internal sealed class PackageRunnerPools : IAsyncDisposable
{
    private readonly EgressProxy _proxy;
    private readonly PackageRunners? _dotnet;
    private readonly PackageRunners _node;
    private readonly PackageRunners _python;
    private readonly PackageRunners _application;

    public PackageRunnerPools(PackageIsolation isolation, ApplicationPackageSettings application, bool allowHttp)
    {
        ArgumentNullException.ThrowIfNull(isolation);
        ArgumentNullException.ThrowIfNull(application);

        _proxy = new EgressProxy(isolation.ProxyPort, allowHttp);
        _dotnet = isolation.InProcess
            ? null
            : new PackageRunners(PackageIsolation.DotnetRunnerSetting, isolation.DotnetHost, isolation.DotnetPort, _proxy);
        _node = new PackageRunners(PackageIsolation.NodeRunnerSetting, isolation.NodeHost, isolation.NodePort, _proxy);
        _python = new PackageRunners(PackageIsolation.PythonRunnerSetting, isolation.PythonHost, isolation.PythonPort, _proxy);
        _application = new PackageRunners(ApplicationPackageSettings.RunnerSetting, application.RunnerHost, application.RunnerPort, _proxy);
    }

    /// <summary>The pool of <c>application-package-host</c>, which runs the application's own packages, whatever their language.</summary>
    public PackageRunners Application => _application;

    /// <summary>The pool that runs the packages of <paramref name="language"/>, or <see langword="null"/> when the worker runs them itself or cannot run them.</summary>
    public PackageRunners? For(PackageLanguage language) =>
        language switch
        {
            PackageLanguage.CSharp => _dotnet,
            PackageLanguage.Node => _node,
            PackageLanguage.Python => _python,
            _ => null,
        };

    /// <summary>Stops the idle runners of each pool, then the proxy.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_dotnet is not null)
        {
            await _dotnet.DisposeAsync();
        }

        await _node.DisposeAsync();
        await _python.DisposeAsync();
        await _application.DisposeAsync();
        await _proxy.DisposeAsync();
    }
}

/// <summary>One connection to a runner. Lines are UTF-8 without a byte order mark, ended by <c>\n</c>.</summary>
internal sealed class RunnerConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _proxy;
    private readonly string _token;
    private readonly EgressProxy _egress;

    public RunnerConnection(TcpClient client, string package, string version, string proxy, string token, EgressProxy egress, string? tenant = null, string? build = null)
    {
        Package = package;
        Version = version;
        Tenant = tenant;
        Build = build;
        _proxy = proxy;
        _token = token;
        _egress = egress;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var stream = client.GetStream();
        _client = client;
        _reader = new StreamReader(stream, utf8);
        _writer = new StreamWriter(stream, utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public string Package { get; }

    public string Version { get; }

    /// <summary>The tenant whose application package the runner serves, or <see langword="null"/> for an installed package.</summary>
    public string? Tenant { get; }

    /// <summary>The digest of the built files the runner was started from, or <see langword="null"/> for an installed package.</summary>
    public string? Build { get; }

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

    /// <summary>
    /// Sends <c>start</c> and waits for <c>ready</c>. The <c>start</c> of an application's package carries its tenant and
    /// <paramref name="files"/>, which the runner is started from.
    /// </summary>
    /// <exception cref="IOException">The host closed the connection or answered something else.</exception>
    public async Task StartAsync(IReadOnlyDictionary<string, byte[]>? files, CancellationToken cancellationToken)
    {
        var start = new StartMessage(Package, Version, _proxy, _token, Tenant, files?.ToDictionary(file => file.Key, file => file.Value));
        await _writer.WriteLineAsync(RunnerProtocol.Write(start).AsMemory(), cancellationToken);
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

    /// <summary>Lets the runner's egress proxy admit the hosts of <paramref name="urls"/>, the <c>url</c> bindings of the call about to run.</summary>
    public void Admit(IEnumerable<Uri> urls) => _egress.Admit(_token, urls);

    /// <summary>Lets the proxy admit nothing for the runner. Returns whether it refused a request since <see cref="Admit"/>.</summary>
    public bool EndCall() => _egress.EndCall(_token);

    public void Dispose()
    {
        _egress.Remove(_token);
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
