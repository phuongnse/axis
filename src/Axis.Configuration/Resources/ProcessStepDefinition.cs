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

    /// <summary>The built-in operation that calls an operation of a connector.</summary>
    public const string CallConnector = "callConnector";

    /// <summary>The built-in operation that raises an event about the subject record.</summary>
    public const string RaiseEvent = "raiseEvent";

    /// <summary>The built-in operation that sends a notification to people.</summary>
    public const string Notify = "notify";

    public required string Name { get; init; }

    /// <summary>The step type: <c>decision</c>, <c>task</c>, <c>operation</c> or <c>end</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The ordered branches of a decision, or null for any other step.</summary>
    public IReadOnlyList<DecisionBranchDefinition>? Branches { get; init; }

    /// <summary>The step a decision takes when no branch is true, or null for any other step.</summary>
    public string? Otherwise { get; init; }

    /// <summary>The operation an operation step runs, such as <c>updateRecord</c>, <c>callConnector</c>, <c>raiseEvent</c> or <c>notify</c>, or null for any other step.</summary>
    public string? Operation { get; init; }

    /// <summary>
    /// The fields of the subject record an <c>updateRecord</c> step sets, each to an expression, in file
    /// order, or null for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Set { get; init; }

    /// <summary>The connector operation a <c>callConnector</c> step calls, or null for any other step.</summary>
    public ConnectorStepReference? Connector { get; init; }

    /// <summary>
    /// The inputs of the connector operation a <c>callConnector</c> step sends, each to an
    /// expression, in file order, or null for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Input { get; init; }

    /// <summary>
    /// The outputs of the connector operation a <c>callConnector</c> step writes, each to a field
    /// of the subject record, in file order, or null when it writes none or for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Output { get; init; }

    /// <summary>The retry policy of a <c>callConnector</c> step, or null for the defaults or for any other step.</summary>
    public RetryDefinition? Retry { get; init; }

    /// <summary>The event a <c>raiseEvent</c> step raises, or null for any other step.</summary>
    public string? Event { get; init; }

    /// <summary>
    /// The payload fields of the event a <c>raiseEvent</c> step raises, each to an expression, in
    /// file order, or null for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Fields { get; init; }

    /// <summary>The recipients of a <c>notify</c> step, in file order, or null for any other step.</summary>
    public IReadOnlyList<NotifyRecipientDefinition>? To { get; init; }

    /// <summary>The channels a <c>notify</c> step sends on, <c>inApp</c> or <c>email</c>, in file order, or null for any other step.</summary>
    public IReadOnlyList<string>? Channels { get; init; }

    /// <summary>The subject, body and placeholder values of a <c>notify</c> step, or null for any other step.</summary>
    public NotifyMessageDefinition? Message { get; init; }

    /// <summary>The page a <c>notify</c> step's notification opens, or null when it opens none or for any other step.</summary>
    public NotifyOpenDefinition? Open { get; init; }

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
