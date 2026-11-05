namespace Axis.ControlPlane.Cells;

/// <summary>
/// The timings of the cell clients. They are code defaults that tests override, not <c>ControlPlane:</c> settings.
/// </summary>
internal sealed class CellClientOptions
{
    /// <summary>A token is renewed this long before it expires.</summary>
    public TimeSpan TokenRefreshMargin { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The wait after the first poll of a job. It doubles after each poll.</summary>
    public TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxPollDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long following one job may take before it throws <see cref="TimeoutException"/>.</summary>
    public TimeSpan FollowTimeout { get; init; } = TimeSpan.FromMinutes(30);
}
