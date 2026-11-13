using Axis.Extensions;

namespace TestPackage;

/// <summary>
/// Ends its own process, to stand in for a runner that crashes. It does so only in a package runner and
/// throws anywhere else, so it can never end a worker or a test process.
/// </summary>
public sealed class ExitOperation : IOperation
{
    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (Environment.GetCommandLineArgs() is not [_, "runner", ..])
        {
            throw new InvalidOperationException("The exit operation ends only a package runner.");
        }

        Environment.Exit(3);
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }
}
