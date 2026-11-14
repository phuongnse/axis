using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Axis.Configuration.Tests;
using Axis.PackageHost;
using Axis.PackageHost.Tests;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// The package host of <c>Axis.PackageHost</c> in this process, on a loopback port, with real C#, Node.js and Python runner
/// processes that run the versions installed under <c>extensionsRoot</c>. It logs to <see cref="Logs"/>, which hold the process
/// id of each runner it started.
/// </summary>
internal sealed partial class TestPackageHost : IAsyncDisposable
{
    private readonly RunnerHost _host;
    private readonly ILoggerFactory _loggers;

    private TestPackageHost(RunnerHost host, ILoggerFactory loggers, IPEndPoint endpoint, LogCollector logs)
    {
        _host = host;
        _loggers = loggers;
        Endpoint = endpoint;
        Logs = logs;
    }

    /// <summary>The address the host listens on.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>The address as <c>host:port</c>, the form of <c>Extensions:Runners:Dotnet</c>, <c>Extensions:Runners:Node</c> and <c>Extensions:Runners:Python</c>.</summary>
    public string Address => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"127.0.0.1:{Endpoint.Port}");

    /// <summary>The log entries of the host.</summary>
    public LogCollector Logs { get; }

    /// <summary>Starts the host. <paramref name="sandbox"/> is what answers a <c>sandbox</c> question, and defaults to the sandbox of this machine.</summary>
    public static async Task<TestPackageHost> StartAsync(string extensionsRoot, Sandbox? sandbox = null)
    {
        var logs = new LogCollector();
        var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var host = new RunnerHost(extensionsRoot, TestRunnerCommand.Create(), loggers.CreateLogger<RunnerHost>(), sandbox);
        return new TestPackageHost(host, loggers, await host.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), CancellationToken.None), logs);
    }

    /// <summary>
    /// Copies the sample packages into <paramref name="folder"/>, installs <c>payment-terms</c> from npm's offline cache, and starts a
    /// host on the copy, see docs/reference/extension-packages.md#sample-and-test-packages.
    /// </summary>
    public static async Task<TestPackageHost> StartOnSamplesAsync(string folder, CancellationToken cancellationToken)
    {
        PackageBuildCommand.Copy(SamplePackages.Path, folder, skipTopLevel: "payment-terms");
        var install = await NodeInstall.InstallAsync(
            Path.Combine(AppContext.BaseDirectory, "SamplePaymentTerms"),
            Path.Combine(folder, "payment-terms", "1.0.0"),
            cancellationToken);
        if (install.Failure is not null)
        {
            throw new InvalidOperationException(install.Failure);
        }

        return await StartAsync(folder);
    }

    /// <summary>The process ids of the runners the host started for <paramref name="package"/> <paramref name="version"/>.</summary>
    public IReadOnlyList<int> RunnerIds(string package, string version) =>
        [.. Logs.Entries
            .Select(entry => RunnerStarted().Match(entry))
            .Where(match => match.Success && match.Groups["package"].Value == package && match.Groups["version"].Value == version)
            .Select(match => int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>What the host answered to each <c>sandbox</c> question, whether it runs under gVisor.</summary>
    public IReadOnlyList<bool> SandboxAnswers() =>
        [.. Logs.Entries
            .Select(entry => SandboxAnswered().Match(entry))
            .Where(match => match.Success)
            .Select(match => bool.Parse(match.Groups["gvisor"].Value))];

    /// <summary>A loopback port where nothing listens, as <c>host:port</c>.</summary>
    public static string ClosedAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
    }

    /// <summary>Whether the process <paramref name="id"/> is gone.</summary>
    public static bool HasExited(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _loggers.Dispose();
    }

    [GeneratedRegex(@"Answered a sandbox question: gVisor (?<gvisor>True|False)\.")]
    private static partial Regex SandboxAnswered();

    [GeneratedRegex(@"Runner (?<id>\d+) started for package (?<package>\S+) (?<version>\S+)\.")]
    private static partial Regex RunnerStarted();
}
