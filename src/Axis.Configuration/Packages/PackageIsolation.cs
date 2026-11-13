using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Axis.Configuration.Packages;

/// <summary>
/// Where the worker runs the operations of a C# package, see
/// docs/reference/extension-packages.md#package-runners. <see cref="InProcess"/> loads each package into
/// the worker. Otherwise the worker sends each attempt to a runner of the package host at
/// <see cref="DotnetHost"/> and <see cref="DotnetPort"/>, which are <see langword="null"/> and 0 when
/// <c>Extensions:Runners:Dotnet</c> is not set.
/// </summary>
public sealed record PackageIsolation(bool InProcess, string? DotnetHost, int DotnetPort)
{
    public const string IsolationSetting = "Extensions:Isolation";
    public const string DotnetRunnerSetting = "Extensions:Runners:Dotnet";

    private const string ProcessValue = "process";
    private const string InProcessValue = "inProcess";

    /// <summary>
    /// Reads <c>Extensions:Isolation</c>, <c>process</c> or <c>inProcess</c> and <c>process</c> when unset, and
    /// <c>Extensions:Runners:Dotnet</c>, a <c>host:port</c> or <c>[ipv6]:port</c> with a port from 1 to 65535.
    /// </summary>
    /// <exception cref="InvalidOperationException">A setting has another value. The message names the setting.</exception>
    public static PackageIsolation Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var inProcess = configuration[IsolationSetting] switch
        {
            null or "" or ProcessValue => false,
            InProcessValue => true,
            _ => throw new InvalidOperationException($"'{IsolationSetting}' must be '{ProcessValue}' or '{InProcessValue}'."),
        };

        if (configuration[DotnetRunnerSetting] is not { Length: > 0 } runner)
        {
            return new PackageIsolation(inProcess, null, 0);
        }

        var separator = runner.LastIndexOf(':');
        var host = separator > 0 ? runner[..separator] : string.Empty;
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        if (host.Length == 0
            || host.Contains(' ', StringComparison.Ordinal)
            || !int.TryParse(runner[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"'{DotnetRunnerSetting}' must be a host and a port from 1 to 65535, as in 'host:port'.");
        }

        return new PackageIsolation(inProcess, host, port);
    }
}
