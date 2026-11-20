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

    /// <summary>
    /// The error as a code with its values, as JSON text: <c>code</c> and <c>values</c>, see
    /// docs/reference/process-inspection.md#step-errors-in-inspection. <see langword="null"/> when the
    /// step succeeded and for a fixed text that has no code.
    /// </summary>
    public string? Problem { get; init; }

    /// <summary>
    /// The subject fields a decision's branch conditions read, as JSON text: <c>name</c> and <c>value</c> per field.
    /// A field that the pinned release marks sensitive holds its name and no value. <see langword="null"/> for other
    /// steps and for rows written before the column. Inspection shows it only under the masking rule, see
    /// docs/reference/process-tables.md#step-history. It is never copied to <see cref="Output"/>, audit records or logs.
    /// </summary>
    public string? Read { get; init; }

    /// <summary>
    /// The subject fields an <c>updateRecord</c> step changed, as JSON text: <c>field</c>, <c>before</c> and <c>after</c>.
    /// A field that the pinned release marks sensitive holds its name and no value. <see langword="null"/> for other
    /// steps and for rows written before the column. Inspection shows it only under the masking rule, see
    /// docs/reference/process-tables.md#step-history. It is never copied to <see cref="Output"/>, audit records or logs.
    /// </summary>
    public string? Changes { get; init; }

    /// <summary>
    /// The recipients a <c>notify</c> step reached, as JSON text: <c>kind</c>, <c>recipient</c> and <c>value</c> per recipient, in the order
    /// the step lists them. An email address that the pinned release marks sensitive, or that cannot be checked, holds no <c>value</c>.
    /// <see langword="null"/> for other steps and for rows written before the column. Inspection shows it only under the masking rule,
    /// see docs/reference/process-tables.md#step-history. It is never copied to <see cref="Output"/>, audit records or logs.
    /// </summary>
    public string? Notified { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    /// <summary>The trace id of the run that executed the step, or <see langword="null"/> when no span was current.</summary>
    public string? TraceId { get; init; }
}
