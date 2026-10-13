using System.Reflection;
using Axis.Extensions;

namespace TestPackage;

/// <summary>Greets <c>name</c> and returns the version of the package build it runs from.</summary>
public sealed class DescribeOperation : IOperation
{
    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var version = typeof(DescribeOperation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        IReadOnlyDictionary<string, object?> output = new Dictionary<string, object?>
        {
            ["greeting"] = $"Hello, {input["name"]}.",
            ["version"] = version,
        };
        return Task.FromResult(output);
    }
}
