namespace Axis.Integration.Tests;

/// <summary>
/// Samples the live managed heap on a thread of its own until stopped, and keeps the highest reading. Each reading
/// follows a full collection, so only memory that is still in use counts. Garbage that the collector has not yet
/// freed, which depends on the machine's collector budget, does not. A sampling thread slows the work it measures,
/// so use it where time is not measured.
/// </summary>
internal sealed class LiveHeapSampler
{
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(10);

    private readonly Thread _thread;
    private volatile bool _running = true;
    private long _peak;

    public LiveHeapSampler(long baseline)
    {
        _peak = baseline;
        _thread = new Thread(Sample) { IsBackground = true };
        _thread.Start();
    }

    /// <summary>The managed heap in use after a full blocking collection.</summary>
    public static long Measure()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(false);
    }

    /// <summary>Takes one last reading, stops the thread and returns the highest reading.</summary>
    public long Stop()
    {
        _running = false;
        _thread.Join();
        _peak = Math.Max(_peak, Measure());
        return _peak;
    }

    private void Sample()
    {
        while (_running)
        {
            _peak = Math.Max(_peak, Measure());
            Thread.Sleep(_interval);
        }
    }
}
