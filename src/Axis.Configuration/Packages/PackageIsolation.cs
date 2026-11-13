using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Axis.Configuration.Packages;

/// <summary>
/// Where the worker runs the operations of a package, see
/// docs/reference/extension-packages.md#package-runners. <see cref="InProcess"/> loads each C# package into
/// the worker. Otherwise the worker sends each attempt of a C# package to a runner of the package host at
/// <see cref="DotnetHost"/> and <see cref="DotnetPort"/>, which are <see langword="null"/> and 0 when
/// <c>Extensions:Runners:Dotnet</c> is not set. A <c>node</c> package always goes to a runner of the package host at
/// <see cref="NodeHost"/> and <see cref="NodePort"/>, which are <see langword="null"/> and 0 when
/// <c>Extensions:Runners:Node</c> is not set. <see cref="ProxyPort"/> is the port of the worker's egress proxy.
/// </summary>
public sealed record PackageIsolation(
    bool InProcess,
    string? DotnetHost,
    int DotnetPort,
    string? NodeHost,
    int NodePort,
    int ProxyPort = RunnerProtocol.ProxyPort)
{
    public const string IsolationSetting = "Extensions:Isolation";
    public const string DotnetRunnerSetting = "Extensions:Runners:Dotnet";
    public const string NodeRunnerSetting = "Extensions:Runners:Node";
    public const string ProxySetting = "Extensions:Runners:ProxyPort";

    private const string ProcessValue = "process";
    private const string InProcessValue = "inProcess";

    /// <summary>
    /// Reads <c>Extensions:Isolation</c>, <c>process</c> or <c>inProcess</c> and <c>process</c> when unset, and
    /// <c>Extensions:Runners:Dotnet</c> and <c>Extensions:Runners:Node</c>, each a <c>host:port</c> or <c>[ipv6]:port</c>
    /// with a port from 1 to 65535.
    /// <c>Extensions:Runners:ProxyPort</c> is a whole number from 0 to 65535 and defaults to <see cref="RunnerProtocol.ProxyPort"/>.
    /// Only Development allows <c>inProcess</c>, see docs/reference/extension-packages.md#loading.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A setting has another value, or <c>Extensions:Isolation</c> is <c>inProcess</c> outside Development. The message names the setting.
    /// </exception>
    public static PackageIsolation Load(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var inProcess = ReadInProcess(configuration, environment);
        var proxyPort = ReadProxyPort(configuration);

        var (dotnetHost, dotnetPort) = ReadRunner(configuration, DotnetRunnerSetting);
        var (nodeHost, nodePort) = ReadRunner(configuration, NodeRunnerSetting);
        return new PackageIsolation(inProcess, dotnetHost, dotnetPort, nodeHost, nodePort, proxyPort);
    }

    /// <summary>
    /// Parses a <c>host:port</c> or <c>[ipv6]:port</c> with a port from 1 to 65535. <paramref name="host"/> has no brackets.
    /// The worker and the package host both read their addresses this way.
    /// </summary>
    public static bool TryParseHostPort(string value, out string host, out int port)
    {
        ArgumentNullException.ThrowIfNull(value);

        var separator = value.LastIndexOf(':');
        host = separator > 0 ? value[..separator] : string.Empty;
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        port = 0;
        return host.Length > 0
            && !host.Contains(' ', StringComparison.Ordinal)
            && int.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is >= 1 and <= 65535;
    }

    /// <summary>
    /// Reads only <c>Extensions:Isolation</c>, for a host that never talks to a package host, so that it stops at
    /// start on the same values as the worker.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Extensions:Isolation</c> is not <c>process</c> or <c>inProcess</c>, or is <c>inProcess</c> outside Development.
    /// </exception>
    public static void Check(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        _ = ReadInProcess(configuration, environment);
    }

    private static (string? Host, int Port) ReadRunner(IConfiguration configuration, string setting)
    {
        if (configuration[setting] is not { Length: > 0 } runner)
        {
            return (null, 0);
        }

        return TryParseHostPort(runner, out var host, out var port)
            ? (host, port)
            : throw new InvalidOperationException($"'{setting}' must be a host and a port from 1 to 65535, as in 'host:port'.");
    }

    private static int ReadProxyPort(IConfiguration configuration) =>
        configuration[ProxySetting] switch
        {
            null or "" => RunnerProtocol.ProxyPort,
            var value when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 0 and <= 65535 => port,
            _ => throw new InvalidOperationException($"'{ProxySetting}' must be a whole number from 0 to 65535."),
        };

    private static bool ReadInProcess(IConfiguration configuration, IHostEnvironment environment)
    {
        var inProcess = configuration[IsolationSetting] switch
        {
            null or "" or ProcessValue => false,
            InProcessValue => true,
            _ => throw new InvalidOperationException($"'{IsolationSetting}' must be '{ProcessValue}' or '{InProcessValue}'."),
        };

        if (inProcess && !environment.IsDevelopment())
        {
            throw new InvalidOperationException($"'{IsolationSetting}' may be '{InProcessValue}' only in Development. Use '{ProcessValue}'.");
        }

        return inProcess;
    }
}
