namespace Axis.Worker;

/// <summary>How the worker claims and polls, bound from the <c>Worker</c> configuration section.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How long a claim lasts before another worker can claim the item.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The wait between two polls of a tenant that has work, and the first wait of the back-off of a tenant that has none.
    /// The worker also reads the schema versions and the tenant directory at most this often.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between two polls of an idle tenant. It must be at least <see cref="PollInterval"/>.</summary>
    public TimeSpan IdlePollCeiling { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the worker reads each tenant database for the due work item and pending computed field gauges.</summary>
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The failures of one delivery that make it a dead letter. It must be at least 1.</summary>
    public int DeliveryAttempts { get; set; } = 5;

    /// <summary>How many loops share the active list of tenants with work due. It must be at least 1.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>The work items a tenant of weight 1 runs in one turn, see <c>workerWeight</c>. It must be at least 1.</summary>
    public int Quantum { get; set; } = 10;
}
