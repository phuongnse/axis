namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_step_history</c>: one occurrence of a step of a process instance, with
/// its input, its output, the decision it took and its error. Times use the database clock.
/// </summary>
public sealed class ProcessStepHistoryRow
{
    public required Guid Id { get; init; }

    public required Guid ProcessInstanceId { get; init; }

    /// <summary>The declared name of the step.</summary>
    public required string Step { get; init; }

    /// <summary>The instance revision the step ran at.</summary>
    public required long Revision { get; init; }

    /// <summary>What the step saw, as JSON text: the subject record's id and version. It holds no field values.</summary>
    public required string Input { get; init; }

    /// <summary>What the step produced, as JSON text, or <see langword="null"/> when it failed.</summary>
    public string? Output { get; init; }

    /// <summary>
    /// The branch a decision took: its zero-based index as text, or <c>otherwise</c>.
    /// <see langword="null"/> for other steps and for a failed step.
    /// </summary>
    public string? Decision { get; init; }

    /// <summary>Why the step failed, or <see langword="null"/> when it succeeded.</summary>
    public string? Error { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }
}
