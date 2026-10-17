namespace Axis.Scenarios;

/// <summary>The command line entry point. A named class keeps it from clashing with other projects' <c>Program</c>.</summary>
internal static class ScenarioProgram
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        return await ScenarioCommand.RunAsync(args, Console.Out, cancellation.Token);
    }
}
