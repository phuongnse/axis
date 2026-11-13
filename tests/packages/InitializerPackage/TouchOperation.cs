using Axis.Extensions;

namespace InitializerPackage;

/// <summary>Writes <c>static-constructor.marker</c> beside the assembly when its type is first used.</summary>
public sealed class TouchOperation : IOperation
{
    static TouchOperation() =>
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(TouchOperation).Assembly.Location)!, "static-constructor.marker"), "");

    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
}
