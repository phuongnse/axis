using Axis.Scenarios;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await ScenarioCommand.RunAsync(args, Console.Out, cancellation.Token);
