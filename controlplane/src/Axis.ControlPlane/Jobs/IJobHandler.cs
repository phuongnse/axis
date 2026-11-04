namespace Axis.ControlPlane.Jobs;

/// <summary>
/// Runs the jobs of one kind. A runner claims only the kinds it has a handler for. A run is at-least-once: a runner
/// that stops after the side effect but before it marks the job done causes a second run, so a handler must be idempotent.
/// A handler that throws fails the attempt. It writes its audit records in its own transactions.
/// </summary>
internal interface IJobHandler
{
    /// <summary>The kind of job this handler runs.</summary>
    string Kind { get; }

    Task RunAsync(ClaimedJob job, CancellationToken cancellationToken);
}
