namespace Axis.Configuration.Resources;

/// <summary>
/// A process step as written in the process file. The properties a step's <see cref="Type"/> needs
/// are required by the JSON Schema. Whether its transitions name steps of the process is checked
/// by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ProcessStepDefinition
{
    public const string Decision = "decision";

    /// <summary>The <c>task</c> step type: a human task that waits for a person to complete it with one of its outcomes.</summary>
    public const string TaskStep = "task";

    /// <summary>The <c>operation</c> step type. <see cref="Operation"/> is the operation the step runs.</summary>
    public const string OperationStep = "operation";

    public const string End = "end";

    /// <summary>The built-in operation that sets fields of the subject record.</summary>
    public const string UpdateRecord = "updateRecord";

    public required string Name { get; init; }

    /// <summary>The step type: <c>decision</c>, <c>task</c>, <c>operation</c> or <c>end</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The ordered branches of a decision, or null for any other step.</summary>
    public IReadOnlyList<DecisionBranchDefinition>? Branches { get; init; }

    /// <summary>The step a decision takes when no branch is true, or null for any other step.</summary>
    public string? Otherwise { get; init; }

    /// <summary>The operation an operation step runs, such as <c>updateRecord</c>, or null for any other step.</summary>
    public string? Operation { get; init; }

    /// <summary>
    /// The fields of the subject record an operation step sets, each to an expression, in file
    /// order, or null for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Set { get; init; }

    /// <summary>The step an operation step leads to, or null for any other step.</summary>
    public string? Next { get; init; }

    /// <summary>The label of a task, shown in the task inbox and on the task page, or null for any other step.</summary>
    public TextReference? Label { get; init; }

    /// <summary>Who a task is assigned to, or null for any other step.</summary>
    public TaskAssigneeDefinition? Assignee { get; init; }

    /// <summary>The form a task shows over the subject record, or null for any other step.</summary>
    public string? Form { get; init; }

    /// <summary>
    /// The ISO 8601 duration from a task's creation to its due date, or null when the task has no
    /// due date or for any other step.
    /// </summary>
    public string? DueIn { get; init; }

    /// <summary>The outcomes a task is completed with, in file order, or null for any other step.</summary>
    public IReadOnlyList<TaskOutcomeDefinition>? Outcomes { get; init; }
}
