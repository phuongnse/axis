namespace Axis.Integration.Tests;

/// <summary>
/// Measures how far the managed heap grows while it runs, counting only live memory, because a full collection runs
/// before each real reading. A collection pauses the whole process, so the sampler waits at least four times as long as
/// its last collection took, and never less than 100 ms. It collects at most about a fifth of the time, so the work
/// that it measures runs close to normal speed even when the heap is large and a collection is slow.
/// </summary>
internal sealed class LiveHeapSampler : IDisposable
{
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The wait is this many times the last collection's duration.</summary>
    internal const int CollectionShare = 4;

    private readonly Thread _thread;
    private readonly Func<long> _measure;
    private readonly long _baseline;
    private readonly ManualResetEventSlim _stopped = new();
    private long _peak;

    public LiveHeapSampler() : this(GC.GetTotalMemory(true), CollectAndMeasure) { }

    /// <summary>Starts sampling from <paramref name="baseline"/>, taking each reading with <paramref name="measure"/>.</summary>
    internal LiveHeapSampler(long baseline, Func<long> measure)
    {
        _measure = measure;
        _baseline = baseline;
        _peak = baseline;
        _thread = new Thread(Sample) { IsBackground = true };
        _thread.Start();
    }

    /// <summary>How long the sampler waits before its next reading, given how long the last collection took.</summary>
    internal static TimeSpan NextInterval(TimeSpan lastCollection) =>
        lastCollection * CollectionShare > MinimumInterval ? lastCollection * CollectionShare : MinimumInterval;

    /// <summary>Stops sampling and returns the highest live heap size seen, less the size at the start.</summary>
    public long Stop()
    {
        _stopped.Set();
        _thread.Join();
        Read();
        Dispose();
        return _peak - _baseline;
    }

    /// <summary>Releases the stop signal. <see cref="Stop"/> calls it, so a sampler that was stopped needs no disposal.</summary>
    public void Dispose() => _stopped.Dispose();

    private void Sample()
    {
        bool stopped;
        do
        {
            var took = Read();
            stopped = _stopped.Wait(NextInterval(took));
        }
        while (!stopped);
    }

    /// <summary>Takes a reading and returns how long it took.</summary>
    private TimeSpan Read()
    {
        var start = DateTime.UtcNow;
        var size = _measure();
        var took = DateTime.UtcNow - start;
        _peak = Math.Max(_peak, size);
        return took;
    }

    /// <summary>Runs a full blocking collection and returns the live heap size.</summary>
    private static long CollectAndMeasure()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        return GC.GetTotalMemory(false);
    }
}
