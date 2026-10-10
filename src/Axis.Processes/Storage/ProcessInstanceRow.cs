namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_instances</c>: one run of a process for one subject record, pinned to
/// the release that was active when it started.
/// </summary>
public sealed class ProcessInstanceRow
{
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The id of the process resource.</summary>
    public required Guid ProcessId { get; init; }

    public required Guid SubjectEntityId { get; init; }

    /// <summary>The id of the subject record.</summary>
    public required Guid SubjectId { get; init; }

    /// <summary>The release the instance is pinned to.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary><c>running</c>, <c>waiting</c>, <c>completed</c> or <c>failed</c>.</summary>
    public required string State { get; init; }

    /// <summary>Starts at 1 and grows with each change of the instance.</summary>
    public required long Revision { get; init; }

    /// <summary>The declared name of the step the instance runs next.</summary>
    public required string Step { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
