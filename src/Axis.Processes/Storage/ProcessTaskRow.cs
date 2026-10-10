namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_tasks</c>: one human task, created when a process instance reaches a
/// <c>task</c> step. It is <c>open</c> until one decision completes it. Times use the database clock.
/// </summary>
public sealed class ProcessTaskRow
{
    public required Guid Id { get; init; }

    public required Guid ProcessInstanceId { get; init; }

    /// <summary>The id of the process resource.</summary>
    public required Guid ProcessId { get; init; }

    /// <summary>The release the instance is pinned to.</summary>
    public required Guid ReleaseId { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The declared name of the task step.</summary>
    public required string Step { get; init; }

    public required Guid SubjectEntityId { get; init; }

    /// <summary>The id of the subject record.</summary>
    public required Guid SubjectId { get; init; }

    /// <summary><c>user</c> or <c>role</c>.</summary>
    public required string AssigneeKind { get; init; }

    /// <summary>The user id or the role name, as <see cref="AssigneeKind"/> says.</summary>
    public required string Assignee { get; init; }

    /// <summary>The id of the form resource the task is completed on.</summary>
    public required Guid FormId { get; init; }

    /// <summary>The creation time plus the step's <c>dueIn</c>, or <see langword="null"/> when the step has none.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary><c>open</c> or <c>completed</c>.</summary>
    public required string State { get; init; }

    /// <summary>The name of the outcome the task was completed with, or <see langword="null"/> while it is open.</summary>
    public string? Outcome { get; init; }

    /// <summary>The user who completed the task, or <see langword="null"/> while it is open.</summary>
    public string? CompletedBy { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
