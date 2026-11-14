using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;

namespace Axis.Worker;

/// <summary>
/// With <c>Extensions:ApplicationPackages</c> on, asks <c>application-package-host</c> at <c>Extensions:Runners:Application</c>
/// whether it runs under gVisor, see docs/reference/extension-packages.md#an-applications-packages. Only a <c>gvisor</c> of
/// <see langword="true"/> passes. A refused or closed connection, another message and a wait that runs out each count as no answer, which fails the
/// check too. With the setting off the check never connects.
/// </summary>
public sealed partial class ApplicationSandboxCheck(ApplicationPackageSettings settings, ILogger<ApplicationSandboxCheck> logger)
{
    /// <summary>How long the worker waits for the host's answer.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the check. Returns <see langword="null"/> when it passes, or the diagnostic that it logged. It makes one attempt, with
    /// no retry.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="stoppingToken"/> was cancelled.</exception>
    public async Task<string?> RunAsync(TimeSpan wait, CancellationToken stoppingToken)
    {
        if (!settings.On)
        {
            return null;
        }

        var address = $"{settings.RunnerHost}:{settings.RunnerPort}";
        switch (await AskAsync(wait, stoppingToken))
        {
            case true:
                return null;
            case false:
                LogNotGvisor(address, ApplicationPackageSettings.RunnerSetting);
                return $"application-package-host at {address}, set by '{ApplicationPackageSettings.RunnerSetting}', does not run under gVisor.";
            default:
                LogNoAnswer(address, ApplicationPackageSettings.RunnerSetting, wait);
                return $"application-package-host at {address}, set by '{ApplicationPackageSettings.RunnerSetting}', did not answer within {wait} whether it runs under gVisor.";
        }
    }

    /// <summary>The host's answer, or <see langword="null"/> when it gives none.</summary>
    private async Task<bool?> AskAsync(TimeSpan wait, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(wait);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.RunnerHost!, settings.RunnerPort, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(RunnerProtocol.Write(new SandboxMessage()) + "\n"), timeout.Token);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            var line = await reader.ReadLineAsync(timeout.Token);
            return line is not null && RunnerProtocol.Read(line) is SandboxMessage { Gvisor: { } gvisor } ? gvisor : null;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is SocketException or IOException or JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(LogLevel.Critical, "application-package-host at {Address}, set by {Setting}, does not run under gVisor. The worker does not start, because an application's package is untrusted code.")]
    private partial void LogNotGvisor(string address, string setting);

    [LoggerMessage(LogLevel.Critical, "application-package-host at {Address}, set by {Setting}, did not answer within {Wait} whether it runs under gVisor. The worker does not start.")]
    private partial void LogNoAnswer(string address, string setting, TimeSpan wait);
}
