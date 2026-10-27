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
        var baseline = GC.GetTotalMemory(true);

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

        // The region has ended, but the garbage is still on the heap until the sampler's first collection.
        var growth = new LiveHeapSampler().Stop();

        Assert.True(growth < Limit, $"The sampler reported {growth:N0} bytes of growth for garbage.");
    }

    [Fact]
    public void Memory_that_is_held_counts_as_heap_growth()
    {
        var sampler = new LiveHeapSampler();
        var held = new List<byte[]>();
        for (var allocated = 0L; allocated < Allocated; allocated += ChunkSize)
        {
            held.Add(new byte[ChunkSize]);
        }

        var growth = sampler.Stop();
        GC.KeepAlive(held);

        Assert.True(growth >= Limit, $"The sampler reported {growth:N0} bytes of growth for held memory.");
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
