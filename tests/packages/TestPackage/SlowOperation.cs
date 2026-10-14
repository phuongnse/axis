using Axis.Extensions;

namespace TestPackage;

/// <summary>Waits until it is cancelled, so it always runs into its timeout.</summary>
public sealed class SlowOperation : IOperation
{
    public async Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new Dictionary<string, object?>();
    }
}
