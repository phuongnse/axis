namespace Axis.Worker;

/// <summary>How the worker claims and polls, bound from the <c>Worker</c> configuration section.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How long a claim lasts before another worker can claim the item.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the worker waits before it polls again when it found no work.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often the worker reads each tenant database for the due work item and pending computed field gauges.</summary>
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The failures of one delivery that make it a dead letter. It must be at least 1.</summary>
    public int DeliveryAttempts { get; set; } = 5;

    /// <summary>How many loops share the active list of tenants with work due. It must be at least 1.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>The work items a tenant of weight 1 runs in one turn, see <c>workerWeight</c>. It must be at least 1.</summary>
    public int Quantum { get; set; } = 10;
}
