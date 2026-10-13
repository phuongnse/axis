using Axis.Extensions;

namespace TestPackage;

/// <summary>Adds the integers <c>a</c> and <c>b</c>.</summary>
public sealed class SumOperation : IOperation
{
    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        IReadOnlyDictionary<string, object?> output = new Dictionary<string, object?>
        {
            ["total"] = (long)input["a"]! + (long)input["b"]!,
        };
        return Task.FromResult(output);
    }
}
