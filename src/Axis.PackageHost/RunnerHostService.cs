using System.Net;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Hosting;

namespace Axis.PackageHost;

/// <summary>Runs <see cref="RunnerHost"/> on <see cref="RunnerProtocol.Port"/> for the life of the host.</summary>
internal sealed class RunnerHostService(RunnerHost host) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await host.StartAsync(new IPEndPoint(IPAddress.Any, RunnerProtocol.Port), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => host.StopAsync();
}
