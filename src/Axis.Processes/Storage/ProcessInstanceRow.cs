namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_instances</c>: one run of a process for one subject record, or for a
/// set of parameter values when the process has no subject. It is pinned to the release that was
/// active when it started.
/// </summary>
public sealed class ProcessInstanceRow
{
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The id of the process resource.</summary>
    public required Guid ProcessId { get; init; }

    /// <summary>The entity of the subject record, or <see langword="null"/> for a process without a subject.</summary>
    public Guid? SubjectEntityId { get; init; }

    /// <summary>The id of the subject record, or <see langword="null"/> for a process without a subject.</summary>
    public Guid? SubjectId { get; init; }

    /// <summary>The release the instance is pinned to.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary><c>running</c>, <c>waiting</c>, <c>completed</c>, <c>failed</c> or <c>cancelled</c>.</summary>
    public required string State { get; init; }

    /// <summary>Starts at 1 and grows with each change of the instance.</summary>
    public required long Revision { get; init; }

    /// <summary>The declared name of the step the instance runs next.</summary>
    public required string Step { get; init; }

    /// <summary>
    /// The JSON text of the parameter values the instance started with, in the record API's JSON
    /// form, or <see langword="null"/> for an instance with a subject.
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>The instance of the process that started this one with a sub-process step, or <see langword="null"/> for any other start.</summary>
    public Guid? ParentInstanceId { get; init; }

    /// <summary>The declared name of the sub-process step in the parent that started this instance, or <see langword="null"/> when it has no parent.</summary>
    public string? ParentStep { get; init; }

    /// <summary>
    /// The user who answers for the instance's writes: its starter at the start, then the user of each task
    /// completion. It is <see langword="null"/> when no user stands behind the start, as for a schedule.
    /// </summary>
    public string? AccountableUser { get; init; }

    /// <summary>The task whose completion last moved the instance on, or <see langword="null"/> until one completes.</summary>
    public Guid? DecisionTask { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the instance became <c>completed</c> or <c>failed</c>, or <see langword="null"/> while it has not ended.</summary>
    public DateTimeOffset? EndedAt { get; init; }
}
