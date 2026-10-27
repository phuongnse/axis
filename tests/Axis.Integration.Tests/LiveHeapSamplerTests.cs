using System.Runtime.CompilerServices;

namespace Axis.Integration.Tests;

/// <summary>
/// The live heap sampler ignores garbage that the collector has not freed yet and still reports memory that is held.
/// The class runs alone, so no other test allocates beside it.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class LiveHeapSamplerTests
{
    private const long Limit = 64L * 1024 * 1024;
    private const long Allocated = 80L * 1024 * 1024;
    private const int ChunkSize = 8 * 1024;

    [Fact]
    public void Garbage_that_is_not_collected_yet_does_not_count_as_heap_growth()
    {
        var baseline = LiveHeapSampler.Measure();

        // The region keeps the collector from running, so the garbage stays on the heap.
        Assert.True(GC.TryStartNoGCRegion(160L * 1024 * 1024));
        try
        {
            Discard();
        }
        finally
        {
            GC.EndNoGCRegion();
        }

        Assert.True(GC.GetTotalMemory(false) - baseline >= Limit, "A reading without a collection should still see the garbage.");

        var sampler = new LiveHeapSampler(baseline);
        var peak = sampler.Stop();

        Assert.True(peak - baseline < Limit, $"The sampler reported {peak - baseline:N0} bytes of growth for garbage.");
    }

    [Fact]
    public void Memory_that_is_held_counts_as_heap_growth()
    {
        var baseline = LiveHeapSampler.Measure();
        var held = new List<byte[]>();
        for (var allocated = 0L; allocated < Allocated; allocated += ChunkSize)
        {
            held.Add(new byte[ChunkSize]);
        }

        var sampler = new LiveHeapSampler(baseline);
        Thread.Sleep(50);
        var peak = sampler.Stop();
        GC.KeepAlive(held);

        Assert.True(peak - baseline >= Limit, $"The sampler reported {peak - baseline:N0} bytes of growth for held memory.");
    }

    /// <summary>Allocates about 80 MB that nothing references afterwards.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Discard()
    {
        for (var allocated = 0L; allocated < Allocated; allocated += ChunkSize)
        {
            GC.KeepAlive(new byte[ChunkSize]);
        }
    }
}
