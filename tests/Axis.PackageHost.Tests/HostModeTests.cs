using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Axis.Configuration.Tests;

namespace Axis.PackageHost.Tests;

/// <summary>
/// Host mode as a real process, which listens on the port and address that <c>PackageHost:Port</c> and <c>PackageHost:Address</c>
/// name and stops at start when either is malformed, see docs/reference/extension-packages.md#the-runner-protocol.
/// Each wait ends on a state, under a cancellation deadline that only bounds a hang.
/// </summary>
public sealed class HostModeTests
{
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Host_mode_listens_on_the_port_and_address_that_the_settings_name()
    {
        using var extensions = new TemporaryFolder();
        var port = FreePort();
        using var host = StartHost(extensions.Path, ("PackageHost__Address", "127.0.0.1"), ("PackageHost__Port", port.ToString()));
        using var deadline = new CancellationTokenSource(_deadline);
        try
        {
            while (true)
            {
                if (host.Process.HasExited)
                {
                    Assert.Fail($"The host exited with code {host.Process.ExitCode}: {await host.Error}");
                }

                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
                    break;
                }
                catch (SocketException)
                {
                    await Task.Delay(100, deadline.Token);
                }
            }
        }
        finally
        {
            host.Process.Kill(entireProcessTree: true);
            await host.Process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("PackageHost__Port", "abc", "PackageHost:Port")]
    [InlineData("PackageHost__Port", "0", "PackageHost:Port")]
    [InlineData("PackageHost__Port", "65536", "PackageHost:Port")]
    [InlineData("PackageHost__Port", "-1", "PackageHost:Port")]
    [InlineData("PackageHost__Address", "not-an-ip", "PackageHost:Address")]
    public async Task Host_mode_stops_at_start_when_a_setting_is_malformed(string variable, string value, string setting)
    {
        using var extensions = new TemporaryFolder();
        using var host = StartHost(extensions.Path, (variable, value));
        using var deadline = new CancellationTokenSource(_deadline);
        try
        {
            await host.Process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            host.Process.Kill(entireProcessTree: true);
            throw;
        }

        Assert.Equal(2, host.Process.ExitCode);
        Assert.Contains(setting, await host.Error, StringComparison.Ordinal);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HostProcess StartHost(string extensions, params (string Name, string Value)[] settings)
    {
        var command = TestRunnerCommand.Create().CSharp;
        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.ArgumentList.Add("host");
        info.Environment["Extensions__Path"] = extensions;
        foreach (var (name, value) in settings)
        {
            info.Environment[name] = value;
        }

        var process = Process.Start(info)!;
        _ = process.StandardOutput.ReadToEndAsync();
        return new HostProcess(process, process.StandardError.ReadToEndAsync());
    }

    private sealed record HostProcess(Process Process, Task<string> Error) : IDisposable
    {
        public void Dispose() => Process.Dispose();
    }
}
