using System.Reflection;

namespace Axis.Configuration.Tests;

public sealed class PerformanceTestCollectionTests
{
    [Fact]
    public void Every_performance_test_class_is_in_the_performance_collection()
    {
        var performanceClasses = typeof(PerformanceTiming).Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("PerformanceTests", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(performanceClasses);

        var missing = performanceClasses
            .Where(type => type.GetCustomAttribute<CollectionAttribute>()?.Name != PerformanceTiming.Name)
            .Select(type => type.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These performance test classes are not in the '{PerformanceTiming.Name}' collection: {string.Join(", ", missing)}");
    }
}
