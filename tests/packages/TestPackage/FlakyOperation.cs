using System.Collections.Concurrent;
using Axis.Extensions;

namespace TestPackage;

/// <summary>
/// Throws a <see cref="RetryableException"/> for the first <c>failures</c> attempts of each operation
/// identity, then returns the attempts it counted for that identity.
/// </summary>
public sealed class FlakyOperation : IOperation
{
    private static readonly ConcurrentDictionary<Guid, int> _attempts = new();

    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var count = _attempts.AddOrUpdate(context.OperationId, 1, (_, previous) => previous + 1);
        if (count <= (long)input["failures"]!)
        {
            throw new RetryableException($"Attempt {count} of {input["failures"]} failures is not through yet.");
        }

        IReadOnlyDictionary<string, object?> output = new Dictionary<string, object?>
        {
            ["attempts"] = (long)count,
        };
        return Task.FromResult(output);
    }
}
