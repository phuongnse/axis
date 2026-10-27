using System.Runtime;

namespace Axis.Integration.Tests;

/// <summary>
/// The heap measurement behind the export test counts memory that is still in use and ignores garbage that the
/// collector has not freed yet. The class runs alone, so no other test holds memory during a measurement.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class HeapGrowthMeasurementTests
{
    private const int ArraySize = 8 * 1024;
    private const long Allocated = 96L * 1024 * 1024;
    private const long Limit = 64L * 1024 * 1024;

    [Fact]
    public void Garbage_left_on_the_heap_does_not_count_as_growth()
    {
        var sampler = new LiveHeapSampler();
        long growth;
        try
        {
            // A no-GC region keeps the garbage on the heap, as a runner with a large gen0 budget does.
            Assert.True(GC.TryStartNoGCRegion(128L * 1024 * 1024));
            for (long total = 0; total < Allocated; total += ArraySize)
            {
                GC.KeepAlive(new byte[ArraySize]);
            }
        }
        finally
        {
            // The sampler stops first, because its forced collection ends the region.
            growth = sampler.Stop();
            if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            {
                GC.EndNoGCRegion();
            }
        }

        Assert.True(growth < Limit, $"Dropped garbage counted as {growth:N0} bytes of growth.");
    }

    [Fact]
    public void Memory_held_during_the_measurement_counts_as_growth()
    {
        var sampler = new LiveHeapSampler();
        var held = new List<byte[]>();
        for (long total = 0; total < Allocated; total += ArraySize)
        {
            held.Add(new byte[ArraySize]);
        }

        var growth = sampler.Stop();
        GC.KeepAlive(held);

        Assert.True(growth >= Limit, $"Held memory counted as only {growth:N0} bytes of growth.");
    }
}
