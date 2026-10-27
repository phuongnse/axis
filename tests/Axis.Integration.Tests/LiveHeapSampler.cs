namespace Axis.Integration.Tests;

/// <summary>Measures how far the managed heap grows while it runs, counting only live memory, because a full collection runs before each reading.</summary>
internal sealed class LiveHeapSampler
{
    private readonly Thread _thread;
    private readonly long _baseline;
    private volatile bool _running = true;
    private long _peak;

    public LiveHeapSampler()
    {
        _baseline = GC.GetTotalMemory(true);
        _peak = _baseline;
        _thread = new Thread(Sample) { IsBackground = true };
        _thread.Start();
    }

    /// <summary>Stops sampling and returns the highest live heap size seen, less the size at the start.</summary>
    public long Stop()
    {
        _running = false;
        _thread.Join();
        Read();
        return _peak - _baseline;
    }

    private void Sample()
    {
        while (_running)
        {
            Read();
            Thread.Sleep(20);
        }
    }

    private void Read()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        _peak = Math.Max(_peak, GC.GetTotalMemory(false));
    }
}
