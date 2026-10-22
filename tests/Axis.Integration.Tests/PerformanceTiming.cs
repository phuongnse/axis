namespace Axis.Integration.Tests;

/// <summary>
/// The performance tests run one class at a time, after the parallel collections, so their time limits measure Axis,
/// not the tests running beside them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceTiming
{
    public const string Name = "Performance tests";
}
