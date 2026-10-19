using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace Axis.Admin;

/// <summary>The command line entry point. A named class keeps it from clashing with other projects' <c>Program</c>.</summary>
internal static class AdminProgram
{
    private static async Task<int> Main(string[] args)
    {
        // A file in /run/secrets, named as a setting in its environment variable form, wins over the variable.
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddKeyPerFile("/run/secrets", optional: true)
            .Build();
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });

        return await AdminCommands.RunAsync(args, configuration, Console.Out, Console.Error, cancellation.Token);
    }
}
