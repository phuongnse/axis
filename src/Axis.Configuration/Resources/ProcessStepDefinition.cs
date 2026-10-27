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

    /// <summary>The <c>subProcess</c> step type: it starts another process and waits for it to end.</summary>
    public const string SubProcess = "subProcess";

    public const string End = "end";

    /// <summary>The <c>timer</c> step type: the process waits for a fixed time (<see cref="Wait"/>) or until a date-time (<see cref="Until"/>), then goes on at <see cref="Next"/>.</summary>
    public const string Timer = "timer";

    /// <summary>The <c>waitEvent</c> step type: the process waits for an event about the subject record (<see cref="Event"/>), then goes on at <see cref="Next"/>, or at the step of <see cref="Timeout"/> when none arrives in time.</summary>
    public const string WaitEvent = "waitEvent";

    /// <summary>The built-in operation that sets fields of the subject record.</summary>
    public const string UpdateRecord = "updateRecord";

    /// <summary>The built-in operation that calls an operation of a connector.</summary>
    public const string CallConnector = "callConnector";

    /// <summary>The built-in operation that raises an event about the subject record.</summary>
    public const string RaiseEvent = "raiseEvent";

    /// <summary>The built-in operation that creates a record of another root entity.</summary>
    public const string CreateRecord = "createRecord";

    /// <summary>The built-in operation that sends a notification to people.</summary>
    public const string Notify = "notify";

    public required string Name { get; init; }

    /// <summary>The step type: <c>decision</c>, <c>task</c>, <c>operation</c>, <c>subProcess</c>, <c>end</c>, <c>timer</c> or <c>waitEvent</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The ordered branches of a decision, or null for any other step.</summary>
    public IReadOnlyList<DecisionBranchDefinition>? Branches { get; init; }

    /// <summary>The step a decision takes when no branch is true, or null for any other step.</summary>
    public string? Otherwise { get; init; }

    /// <summary>The operation an operation step runs, such as <c>updateRecord</c>, <c>callConnector</c>, <c>raiseEvent</c>, <c>createRecord</c> or <c>notify</c>, or null for any other step.</summary>
    public string? Operation { get; init; }

    /// <summary>
    /// The fields of the subject record an <c>updateRecord</c> step sets, or of the record a
    /// <c>createRecord</c> step creates, each to an expression, in file order, or null for any other step.
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

    /// <summary>The event a <c>raiseEvent</c> step raises or a <c>waitEvent</c> step waits for, or null for any other step.</summary>
    public string? Event { get; init; }

    /// <summary>
    /// The payload fields of the event a <c>raiseEvent</c> step raises, each to an expression, in
    /// file order, or null for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Fields { get; init; }

    /// <summary>The root entity a <c>createRecord</c> step creates a record of, or null for any other step.</summary>
    public string? Entity { get; init; }

    /// <summary>The recipients of a <c>notify</c> step, in file order, or null for any other step.</summary>
    public IReadOnlyList<NotifyRecipientDefinition>? To { get; init; }

    /// <summary>The channels a <c>notify</c> step sends on, <c>inApp</c> or <c>email</c>, in file order, or null for any other step.</summary>
    public IReadOnlyList<string>? Channels { get; init; }

    /// <summary>The subject, body and placeholder values of a <c>notify</c> step, or null for any other step.</summary>
    public NotifyMessageDefinition? Message { get; init; }

    /// <summary>The page a <c>notify</c> step's notification opens, or null when it opens none or for any other step.</summary>
    public NotifyOpenDefinition? Open { get; init; }

    /// <summary>The name of the process a <c>subProcess</c> step starts, or null for any other step.</summary>
    public string? Process { get; init; }

    /// <summary>
    /// The expression that gives the record a <c>subProcess</c> step starts its process for, or null
    /// when the child has no subject, or the same subject as the parent, or for any other step.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The parameters of the process a <c>subProcess</c> step starts, each to an expression, in file
    /// order, or null for any other step.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>The step an operation, <c>subProcess</c>, <c>timer</c> or <c>waitEvent</c> step leads to, or null for any other step.</summary>
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

    /// <summary>
    /// The ISO 8601 duration a <c>timer</c> step waits, or null when it waits until a time or for any
    /// other step. Exactly one of <see cref="Wait"/> and <see cref="Until"/> is set on a timer.
    /// </summary>
    public string? Wait { get; init; }

    /// <summary>
    /// The expression of type <c>date-time</c> that gives the time a <c>timer</c> step waits until, or
    /// null when it waits for a duration or for any other step.
    /// </summary>
    public string? Until { get; init; }

    /// <summary>The outcomes a task is completed with, in file order, or null for any other step.</summary>
    public IReadOnlyList<TaskOutcomeDefinition>? Outcomes { get; init; }

    /// <summary>When a task expires and the step it then leads to, or null when the task has no deadline or for any other step.</summary>
    public TaskDeadlineDefinition? Deadline { get; init; }

    /// <summary>What happens to a task before its deadline, in file order, or null when the task has none or for any other step.</summary>
    public IReadOnlyList<TaskEscalationDefinition>? Escalations { get; init; }

    /// <summary>
    /// The expression of type <c>boolean</c> over the payload of the event a <c>waitEvent</c> step
    /// waits for, or null when any such event ends the wait or for any other step.
    /// </summary>
    public string? When { get; init; }

    /// <summary>How long a <c>waitEvent</c> step waits and the step it then leads to, or null when it waits for as long as it takes or for any other step.</summary>
    public WaitTimeoutDefinition? Timeout { get; init; }
}
