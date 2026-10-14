using Axis.Extensions;

namespace TestPackage;

/// <summary>Throws a <see cref="FormatException"/> whose message holds the input <c>a</c>.</summary>
public sealed class FailOperation : IOperation
{
    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        throw new FormatException($"The input {input["a"]} is not valid.");
    }
}
