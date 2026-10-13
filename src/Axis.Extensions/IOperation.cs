namespace Axis.Extensions;

/// <summary>
/// An operation of an extension package: the class an operation's <c>type</c> names in the
/// package manifest. The host creates it through its public parameterless constructor.
/// </summary>
public interface IOperation
{
    /// <summary>Runs the operation. The input and the output are keyed by field name.</summary>
    Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken);
}
