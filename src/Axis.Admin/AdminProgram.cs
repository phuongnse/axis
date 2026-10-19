using Microsoft.Extensions.Configuration;

namespace Axis.Admin;

/// <summary>The command line entry point. A named class keeps it from clashing with other projects' <c>Program</c>.</summary>
internal static class AdminProgram
{
    private static async Task<int> Main(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        return await AdminCommands.RunAsync(args, configuration, Console.Out, Console.Error, cancellation.Token);
    }
}
