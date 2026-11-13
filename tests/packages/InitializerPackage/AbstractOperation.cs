using Axis.Extensions;

namespace InitializerPackage;

/// <summary>An abstract class that implements <c>IOperation</c>, which a manifest can name as an operation type to be refused.</summary>
public abstract class AbstractOperation : IOperation
{
    public abstract Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken);
}
