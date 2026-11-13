using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>The command line entry point. A named class keeps it from clashing with other projects' <c>Program</c>.</summary>
internal static class PackageHostProgram
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        return await PackageBuildCommand.RunAsync(args, configuration, Console.Out, cancellation.Token);
    }
}
