namespace Axis.ControlPlane.Jobs;

/// <summary>
/// The timings of the job runner. They are code defaults that tests override, not <c>ControlPlane:</c> settings.
/// </summary>
internal sealed class JobRunnerOptions
{
    /// <summary>How long a claim lasts. The runner renews it every third of this time while a handler runs.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the runner waits when it claimed nothing.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The delay after the first failed attempt. It doubles after each one.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromHours(1);

    /// <summary>The attempts a job gets. The job is <c>failed</c> after this many failed attempts.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>The delay before the attempt after <paramref name="attempt"/> failed: <see cref="BaseDelay"/> times 2 to the power of <c>attempt - 1</c>, at most <see cref="MaxDelay"/>.</summary>
    public TimeSpan RetryDelay(int attempt)
    {
        var seconds = BaseDelay.TotalSeconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 30));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }
}
