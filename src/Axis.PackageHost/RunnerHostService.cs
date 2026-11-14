using System.Globalization;
using System.Net;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Axis.PackageHost;

/// <summary>
/// Runs <see cref="RunnerHost"/> on the endpoint that <see cref="TryReadEndpoint"/> reads for the life of the host, see
/// docs/reference/extension-packages.md#the-runner-protocol.
/// </summary>
internal sealed class RunnerHostService(RunnerHost host, IPEndPoint bind) : IHostedService
{
    /// <summary>The setting for the listen port, a whole number from 1 to 65535 that defaults to <see cref="RunnerProtocol.Port"/>.</summary>
    public const string PortSetting = "PackageHost:Port";

    /// <summary>The setting for the listen address, an IP address that defaults to every interface.</summary>
    public const string AddressSetting = "PackageHost:Address";

    /// <summary>Reads the listen endpoint. When a setting is malformed, <paramref name="error"/> names it and the host must not start.</summary>
    public static bool TryReadEndpoint(IConfiguration configuration, out IPEndPoint endpoint, out string error)
    {
        endpoint = new IPEndPoint(IPAddress.Any, RunnerProtocol.Port);
        error = "";

        var port = RunnerProtocol.Port;
        if (configuration[PortSetting] is { Length: > 0 } portValue
            && !(int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535))
        {
            error = $"Set '{PortSetting}' to a whole number from 1 to 65535.";
            return false;
        }

        var address = IPAddress.Any;
        if (configuration[AddressSetting] is { Length: > 0 } addressValue && !IPAddress.TryParse(addressValue, out address))
        {
            error = $"Set '{AddressSetting}' to an IP address.";
            return false;
        }

        endpoint = new IPEndPoint(address!, port);
        return true;
    }

    public async Task StartAsync(CancellationToken cancellationToken) => await host.StartAsync(bind, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => host.StopAsync();
}
